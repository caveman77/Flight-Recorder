using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Timers;
using FlightRecorder.Client.SimConnectMSFS;
using Microsoft.Extensions.Logging;
using SharpKml.Dom;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FlightRecorder.Client.Logics;

public class ReplayLogic : IReplayLogic, IDisposable
{
    public event EventHandler<RecordsUpdatedEventArgs>? RecordsUpdated;
    public event EventHandler? ReplayFinished;
    public event EventHandler<CurrentFrameChangedEventArgs>? CurrentFrameChanged;

    private const int EventThrottleMilliseconds = 500;

    private readonly ILogger<ReplayLogic> logger;

    // SimConnect connector interface
    private readonly IConnector connector;

    // Used to give record duration but also replay duration
    private readonly Stopwatch stopwatch = new();

    // Seems to be the start  timing. It can be different from the recording start upon trim
    private long? startMilliseconds;

    // Recording duration (given by Stopwatch)
    private long? endMilliseconds;
    private SimStateStruct? startState;

    // Records of all positions of all aircraft along the time
    public List<List<(long milliseconds, AircraftPositionStruct? position)>> Records { get; private set; } = new();

    // Every minute aircraft status notifications
    public List<List<(long milliseconds, SimStateStruct? position)>> Sorrounding_records { get; private set; } = new ();

    // User Aircraft records
    //public List<(long milliseconds, AircraftPositionStruct? position)> User_Records { get; private set; } = new();

    // list of the ID of the Aircraft as recorded (same order as the orther lists)
    public List<AircraftWithObjectID> AircraftList { get; private set; } = new();

    // ObjectID of the user Aircraft
    public uint UserArcraftID;

    //public string? AircraftTitle { get; set; }
    public List<SimStateStruct> AircraftAddinfo { get; private set; } = new();

    private int currentFrame;

    // Replay speed rate requested by user
    private double rate = 1;

    private bool repeat = false;
    private int? pausedFrame;

    // Replay speed rate save when user pressed pause
    private double? pausedRate;
    private bool isReplayStopping;
    private long? replayMilliseconds;

    // Time between the start of the replay timer and the time when pause has been pressed
    private long? pausedMilliseconds;

    // Offset delay between start and replay start point required by user
    private long offsetStartMilliseconds = 0;
    private bool forceReset = false;

    private AircraftPositionStruct? currentPosition = null;
    private long? lastTriggeredMilliseconds = null;
    private TaskCompletionSource<bool>? tcs;

    public bool IsReplayable => Records.Count > 0;
    private bool IsReplaying => replayMilliseconds != null && pausedMilliseconds == null;
    private bool IsPausing => pausedMilliseconds != null;

    //private bool IsAI([NotNullWhen(true)] string? aircraftTitle) => !string.IsNullOrEmpty(aircraftTitle);

    // Seems to be the Request ID of the request to spawn the AI aircraft
    private List<uint?> aiRequestId = new();

    // The aircraft ID known from MSFS of the spwaned AI Aircraft (ObjectID on replay)
    private List<uint?> aiId = new();
    // private Timer timer;

    public ReplayLogic(ILogger<ReplayLogic> logger, IConnector connector)
    {
        logger.LogDebug("Creating instance of {class}", nameof(ReplayLogic));

        this.logger = logger;
        this.connector = connector;

        RegisterEvents();
    }

    public void Dispose()
    {
        logger.LogDebug("Disposing {class}", nameof(RecorderLogic));
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            DeregisterEvents();
        }
    }

    private void RegisterEvents()
    {
        connector.AircraftIdReceived += Connector_AircraftIdReceived;
        connector.CreatingObjectFailed += Connector_CreatingObjectFailed;
        connector.Frame += Connector_Frame;
    }

    private void DeregisterEvents()
    {
        connector.AircraftIdReceived -= Connector_AircraftIdReceived;
        connector.CreatingObjectFailed -= Connector_CreatingObjectFailed;
        connector.Frame -= Connector_Frame;
    }

    #region Public Functions

    public bool Replay()
    {
        if (!IsReplayable)
        {
            logger.LogInformation("No record to replay!");
            return false;
        }

        logger.LogDebug("Initializing replay...");

        stopwatch.Start();

        logger.LogInformation("Start replay from {currentFrame}...", currentFrame);

        stopwatch.Restart();
        lastTriggeredMilliseconds = null;
        replayMilliseconds = stopwatch.ElapsedMilliseconds - (long)(offsetStartMilliseconds / rate);

        if (Records.Any())
        {
            int i = 0;
            foreach (var aircraftposlist in Records)
            {

                var currentPosition = aircraftposlist[currentFrame].position;
                if (AircraftList[i].objectID != UserArcraftID)
                {
                    aiRequestId[i] = connector.Spawn(AircraftList[i].aircraftStatus.AircraftTitle, currentPosition);
                }
                else
                {
                    connector.Init(0, currentPosition);
                }

                i++;
            }
        }

        Task.Run(RunReplay);

        return true;
    }

    public bool PauseReplay()
    {
        if (IsReplaying)
        {
            logger.LogInformation("Pause recording...");

            pausedMilliseconds = stopwatch.ElapsedMilliseconds;
            pausedFrame = currentFrame;
            pausedRate = rate;

            return true;
        }
        return false;
    }

    /**** Timeline
     * replayMilliseconds
     *                                        pausedMilliseconds
     *                                                            stopwatch
     *                                                            resume
     */

    public bool ResumeReplay()
    {
        if (IsPausing)
        {
            // Recalculate the projected replayMilliseconds (when replay starts) based on current elapsed period and current rate
            var frame = currentFrame;
            if (frame == pausedFrame)
            {
                // No seeking => Resume based on pause time
                if (pausedMilliseconds == null) throw new InvalidOperationException("Cannot resume without pause time!");
                if (replayMilliseconds == null) throw new InvalidOperationException("Cannot resume without replay time!");
                if (pausedRate == null) throw new InvalidOperationException("Cannot resume without pause rate!");
                replayMilliseconds = stopwatch.ElapsedMilliseconds - (long)((pausedMilliseconds - replayMilliseconds) / rate * pausedRate);
            }
            else
            {
                // Resume based on seeked frame
                if (startMilliseconds == null) throw new InvalidOperationException("Cannot resume without start time!");
                replayMilliseconds = stopwatch.ElapsedMilliseconds - (long)((User_Records[frame].milliseconds - startMilliseconds) / rate);
            }

            // Initialize resumed position
            if (frame == -1)
            {
                // Ignore as this happens when Pause is clicked before the first frame is calculated
            }
            else if (frame == pausedFrame)
            {
                // Ignore to prevent init unnecessarily
            }
            else if (frame >= 0 && frame < Records.Count)
            {
                int i = 0;
                foreach (var ai in aiId)
                {
                    connector.Init(ai ?? 0, Records[i][frame].position);

                    i++;
                }
            }
            else
            {
                throw new InvalidOperationException($"Cannot resume at frame {frame} because there are only {Records.Count} frames!");
            }

            // Signal unpaused
            pausedMilliseconds = null;
            // NOTE: pausedFrame is not cleared here to allow resuming in the loop

            return true;
        }
        return false;
    }

    public bool StopReplay()
    {
        if (IsReplaying || IsPausing)
        {
            isReplayStopping = true;

            // Make sure at least one more tick happens to handle sim exit
            Tick();

            return true;
        }
        return false;
    }

    public void Seek(int value)
    {
        logger.LogTrace("Seek to {value} from {current}", value, currentFrame);

        // NOTE: We need to check for change here to avoid unnecessary seeking due to CurrentFrameChanged event from internal logic.
        if (currentFrame != value)
        {
            currentFrame = value;

            if (IsPausing)
            {
                int i = 0;
                foreach (var rec in Records)
                {
                    if (aiId[i] !=null)
                    {
                        (var elapsed, var position) = rec[value];
                        MoveAircraft((uint) aiId[i], elapsed, position, null, null, 0);
                    }

                }

                i++;

            }
            else if (!IsReplaying)
            {
                (var elapsed, var position) = User_Records[value];
                offsetStartMilliseconds = (elapsed - startMilliseconds) ?? 0L;
            }
        }
    }

    public void TrimStart()
    {
        logger.LogInformation("Trim start from frame {frame}", currentFrame);
        var trimFrame = currentFrame;

        if (IsPausing)
        {
            // Move the pause timestamp to the beginning
            pausedMilliseconds = replayMilliseconds;
            pausedFrame = 0;
            forceReset = true;
        }
        else
        {
            offsetStartMilliseconds = 0;
        }

        (var currentElapsed, _) = User_Records[trimFrame];
        startMilliseconds = currentElapsed;
        currentFrame = 0;
        CurrentFrameChanged?.Invoke(this, new(currentFrame));
        Records = Records.Skip(trimFrame).ToList();
        RecordsUpdated?.Invoke(this, new(null, startState?.AircraftTitle, Records.Count));
    }

    public void TrimEnd()
    {
        logger.LogDebug("Trim end from frame {frame}", currentFrame);
        var trimFrame = currentFrame;

        if (!IsPausing)
        {
            offsetStartMilliseconds = 0;
            currentFrame = 0;
            CurrentFrameChanged?.Invoke(this, new(currentFrame));
        }

        Records = Records.Take(trimFrame + 1).ToList();
        RecordsUpdated?.Invoke(this, new(null, startState?.AircraftTitle, Records.Count));
    }

    public void ChangeRate(double rate)
    {
        this.rate = rate;
    }

    public void SetRepeat(bool repeat)
    {
        this.repeat = repeat;
    }

    public void Unfreeze()
    {
        if (replayMilliseconds != null)
        {

            int i = 0;
            foreach (var plane in AircraftList)
            {
                if (plane.objectID == UserArcraftID)
                {
                    connector.Unfreeze(0);
                }
                else
                {
                    if (aiId[i] != null)
                    {
                        connector.Unfreeze((uint) aiId[i]);
                    }
                    
                }

            }


        }
    }

    public void NotifyPosition(AircraftPositionStruct? value)
    {
        currentPosition = value;
    }

    public void FromData(string? fileName, SavedData data)
    {
        startMilliseconds = data.StartTime;
        endMilliseconds = data.EndTime;
        startState = data.StartState == null ? null : SimState.ToStruct(data.StartState);
       

        AircraftList = data.AircraftList;
        UserArcraftID = data.UserArcraftID;
        Reset();

        Records = new List<List<(long milliseconds, AircraftPositionStruct? position)>>();
        //User_Records = new List<(long milliseconds, AircraftPositionStruct? position)>();

        if (data.Records != null)
        {
            int i = 0;
            foreach (var listr in data.Records)
            {
                List<(long Time, AircraftPositionStruct? position)> mylist = new List<(long Time, AircraftPositionStruct? position)>();

                foreach (var record in listr)
                {
                    
                    if (record.Position != null)
                    {
                        mylist.Add(( record.Time, (AircraftPositionStruct?)AircraftPosition.ToStruct(record.Position)));
                    }
                    else
                    {
                        mylist.Add( (record.Time, null) );
                    }


                }

                Records.Add(mylist);

                //if (data.AircraftList[i].objectID == data.UserArcraftID)
                //    User_Records = mylist;

                i++;
            }
        }





        RecordsUpdated?.Invoke(this, new(fileName, data.StartState?.AircraftTitle, Records.Count));
        CurrentFrameChanged?.Invoke(this, new(currentFrame));
    }

    /*
    public SavedData ToData(string clientVersion)
    {
        if (startMilliseconds == null) throw new InvalidOperationException("Invalid replay data without start time!");
        if (endMilliseconds == null) throw new InvalidOperationException("Invalid replay data without end time!");
        return new(clientVersion, startMilliseconds.Value, endMilliseconds.Value, startState, Records);
    }
    */

    #endregion

    #region Private Functions

    private void Connector_AircraftIdReceived(object? sender, AircraftIdReceivedEventArgs e)
    {
        // search RequestID
        var result = Enumerable.Range(0, aiRequestId.Count).Where(i => aiRequestId[i] == e.RequestId).ToList();
        if (result.Count != 1)
        {
            logger.LogError("RequestID is not unic !");
        }
        else
        {
            logger.LogDebug("Set AI ID {objectID}", e.ObjectId);
            aiId[result[0]] = e.ObjectId;
        }
    }

    private void Connector_CreatingObjectFailed(object? sender, EventArgs e)
    {
            logger.LogDebug("Fail to spawn for request");
    }

    private void Connector_Frame(object? sender, EventArgs e)
    {
        Tick();
    }

    private void Timer_Elapsed(object sender, ElapsedEventArgs e)
    {
        //Tick();
    }

    private async Task RunReplay()
    {

        //timer = new Timer();
        //timer.Elapsed += Timer_Elapsed;
        //timer.Start();

        connector.Freeze(0);


        var enumerator = User_Records.GetEnumerator();
        currentFrame = -1;
        long? recordedElapsed = null;
        AircraftPositionStruct? position = null;

        long? lastElapsed = 0;
        AircraftPositionStruct? lastPosition = null;

        while (true)
        {
            // TODO: break this loop when window is closed

            // Wait for tick call from the sim frame
            tcs = new TaskCompletionSource<bool>();
            await tcs.Task;
            tcs = null;

            if (isReplayStopping)
            {
                FinishReplay(false);
                return;
            }

            var replayStartTime = replayMilliseconds;
            if (replayStartTime == null)
            {
                // Safe-guard for Stopped
                continue;
            }

            /* Hoping not to break all
            if (IsAI(AircraftTitle) && aiId == null)
            {
                // Wait for spawning
                continue;
            }
            */

            if (IsPausing)
            {
                continue;
            }

            if (forceReset || (pausedFrame != null && pausedFrame != currentFrame))
            {
                // Reset the enumerator since user might seek backward or change changed due to trimming
                logger.LogDebug("Reset interaction. Pause frame {frame}.", pausedFrame);

                forceReset = false;

                enumerator = User_Records.GetEnumerator();
                currentFrame = -1;
                recordedElapsed = null;
                position = null;

                pausedFrame = null;
            }

            var currentElapsed = (long)((stopwatch.ElapsedMilliseconds - replayStartTime.Value) * rate);

            try
            {
                while (!recordedElapsed.HasValue || currentElapsed > recordedElapsed)
                {
                    logger.LogTrace("Move next {currentElapsed}", currentElapsed);
                    var canMove = enumerator.MoveNext();

                    if (canMove)
                    {
                        currentFrame++;
                        (var recordedMilliseconds, var recordedPosition) = enumerator.Current;
                        lastElapsed = recordedElapsed;
                        lastPosition = position;
                        recordedElapsed = recordedMilliseconds - startMilliseconds;
                        position = recordedPosition;

                        // Try to check the velocity
                    }
                    else
                    {
                        // Last frame
                        FinishReplay(true);
                        return;
                    }
                }
            }
            finally
            {
                logger.LogTrace("Current Frame {currentFrame} {ellapsed}", currentFrame, currentElapsed);
                CurrentFrameChanged?.Invoke(this, new(currentFrame));
            }

            if (recordedElapsed.HasValue)
            {
                int i = 0;
                foreach (var avion in aiId)
                {
                    if (avion!=null)
                        MoveAircraft((uint)avion, recordedElapsed.Value, Records[i][currentFrame].position, lastElapsed, lastPosition, currentElapsed);

                    ++i;
                }

                
            }
        }
    }

    private void FinishReplay(bool reachedLastFrame)
    {
        logger.LogInformation("Replay finished.");

        isReplayStopping = false;

        int i = 0;
        foreach (var avion in AircraftList)
        {
            if (avion.objectID != UserArcraftID)
            {
                if (aiId[i] != null)
                {
                    connector.Despawn((uint)aiId[i]);
                    aiId[i] = null;
                }

            }
            else
            {
                Unfreeze();
            }

            i++;
        }


        Reset();

        if (reachedLastFrame && repeat)
        {
            Replay();
        }
        else
        {
            ReplayFinished?.Invoke(this, new EventArgs());
        }
    }

    private void Reset()
    {
        pausedMilliseconds = null;
        pausedFrame = null;
        replayMilliseconds = null;
        currentFrame = 0;
        offsetStartMilliseconds = 0;


        aiId = new List<uint?>();
        aiRequestId = new List<uint?>();

        if (AircraftList != null)
        {
            foreach (var aircraft in AircraftList)
            {
                if (aircraft.objectID == UserArcraftID)
                    aiId.Add(aircraft.objectID);
                else
                    aiId.Add(null);

                aiRequestId.Add(null);
            }

        }

    }

    private void MoveAircraft(uint dwObjectId, long nextElapsed, AircraftPositionStruct position, long? lastElapsed, AircraftPositionStruct? lastPosition, long currentElapsed)
    {
        logger.LogTrace("Delta time {delta} {current} {recorded}.", currentElapsed - nextElapsed, currentElapsed, nextElapsed);

        var nextValue = AircraftPositionStructOperator.ToSet(position);
        if (lastPosition.HasValue && lastElapsed.HasValue)
        {
            var interpolation = (double)(currentElapsed - lastElapsed.Value) / (nextElapsed - lastElapsed.Value);
            if (interpolation == 0.5)
            {
                // Edge case: let next value win so Math.round does not act unexpectedly
                interpolation = 0.501;
            }
            nextValue = AircraftPositionStructOperator.Interpolate(nextValue, AircraftPositionStructOperator.ToSet(lastPosition.Value), interpolation);
        }
        if ((dwObjectId != UserArcraftID) && currentPosition.HasValue && (lastTriggeredMilliseconds == null || stopwatch.ElapsedMilliseconds > lastTriggeredMilliseconds + EventThrottleMilliseconds))
        {
            lastTriggeredMilliseconds = stopwatch.ElapsedMilliseconds;
            connector.TriggerEvents(currentPosition.Value, position);
        }

        connector.Set(dwObjectId, nextValue);
    }

    private void Tick()
    {
        if (IsReplaying || IsPausing)
        {
            try
            {
                tcs?.SetResult(true);
            }
            catch (InvalidOperationException ex)
            {
                // Ignore since most likely tcs result is already set
                logger.LogDebug(ex, "Cannot set TCS result on tick");
            }
        }
    }

    #endregion
}
