using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Timers;
using FlightRecorder.Client.SimConnectMSFS;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
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

    // User aircraft
    public UserAircraft UserAircraft { get; private set; } = new(); 

    // list of the AI of the Aircraft as recorded (same order as the orther lists)
    public List<AiAircraft> AiAircraftList = new();

    // ObjectID of the user Aircraft
    public uint UserArcraftID;



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

    //private AircraftPositionStruct? currentPosition = null;
    private long? lastTriggeredMilliseconds = null;
    private TaskCompletionSource<bool>? tcs;

    public bool IsReplayable => UserAircraft.Records == null ? false: UserAircraft.Records.Count > 0;
    private bool IsReplaying => replayMilliseconds != null && pausedMilliseconds == null;
    private bool IsPausing => pausedMilliseconds != null;

    //private bool IsAI([NotNullWhen(true)] string? aircraftTitle) => !string.IsNullOrEmpty(aircraftTitle);

    private bool playUserAircraft = false;
    private bool playAiArcraft = false;

    private AiAircraftPositionStruct aidefaultPosition = new AiAircraftPositionStruct
    {
        AIBank = 28.999999999999993,
        AIPitch = -19.999999999999993,
        AbsoluteTime = 63909094529.924271,
        Altitude = 2091.9170057785809,
        AltitudeAboveGround = 9.6935990980027782,
        Bank = 0,
        BrakeLeftPosition = 0,
        BrakeRightPosition = 1,
        BrakeParkingPosition = 1,
        Longitude = -16.447037877832933,
        Latitude = 81.173171823478071
    };

    private AircraftPositionStruct defaultPosition = new AircraftPositionStruct
    {
        AIBank = 28.999999999999993,
        AIPitch = -19.999999999999993,
        AbsoluteTime = 63909094529.924271,
        AccelerationBodyX = 0,
        AccelerationBodyY = 0,
        AccelerationBodyZ = 0,
        AileronPosition = 0,
        AileronTrimPercent = 0,
        Altitude = 2091.9170057785809,
        AltitudeAboveGround = 9.6935990980027782,
        Bank = 0,
        BrakeLeftPosition = 0,
        BrakeRightPosition = 1,
        BrakeParkingPosition = 1,
        Longitude = -16.447037877832933,
        MachAirspeed = 0,
        Latitude = 81.173171823478071
    };



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

    public void SetReplayScope(bool playUserAircraft, bool playAiArcrafts)
    {
        this.playAiArcraft = playAiArcrafts;
        this.playUserAircraft = playUserAircraft;
    }

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

        if (UserAircraft.Records.Any())
        {

            if (this.playUserAircraft)
            {
                AircraftPositionStruct? currentPosition = UserAircraft.Records[currentFrame].position;

                if (currentPosition == null)
                    currentPosition = (AircraftPositionStruct)defaultPosition;

                connector.Init(0, (AircraftPositionStruct)currentPosition);
            }
                

            if (this.playAiArcraft)
            {
                foreach (var aircraft in AiAircraftList)
                {
                    AiAircraftPositionStruct? aicurrentPosition = new AiAircraftPositionStruct?();

                    if ((aircraft.StartIndex <= currentFrame) && (aircraft.StopIndex >= currentFrame))
                        aicurrentPosition = aircraft.Records[currentFrame - aircraft.StartIndex].position;

                    // Would need to search for previous frames to see if the planed didn't moved
                    if (aicurrentPosition == null)
                        aicurrentPosition = (AiAircraftPositionStruct)aidefaultPosition;

                    aircraft.aiRequestId = connector.Spawn(((SimStateStruct)aircraft.AircraftStatus).AircraftTitle, (AiAircraftPositionStruct)aicurrentPosition);
                }

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
                replayMilliseconds = stopwatch.ElapsedMilliseconds - (long)((UserAircraft.Records[frame].milliseconds - startMilliseconds) / rate);
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
            else if (this.playAiArcraft && frame >= 0 && frame < UserAircraft.Records.Count)
            {
                if (this.playUserAircraft)
                    connector.Init(0, defaultPosition);

                if (this.playAiArcraft)
                {
                    foreach (var ai in AiAircraftList)
                    {
                        //var cur_pos = Records[i][frame].position;
                        connector.Init(ai.aiId ?? 0, aidefaultPosition);
                    }
                }
            }
            else
            {
                throw new InvalidOperationException($"Cannot resume at frame {frame} because there are only {UserAircraft.Records.Count} frames!");
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
                if (this.playUserAircraft)
                    MoveAircraft(UserAircraft.UserArcraftID, UserAircraft.Records[value].milliseconds, UserAircraft.Records[value].position, null, null, 0);

                if (this.playAiArcraft)
                {
                    foreach (var avion in AiAircraftList)
                    {
                        if (avion.aiId != null)
                        {
                            if ((avion.StartIndex <= value) && (avion.StopIndex >= value))
                                MoveAiAircraft((uint)avion.aiId, avion.Records[value - avion.StartIndex].milliseconds, avion.Records[value - avion.StartIndex].position, null, null, 0);
                        }

                    }
                }

            }
            else if (!IsReplaying)
            {
                offsetStartMilliseconds = (UserAircraft.Records[value].milliseconds - startMilliseconds) ?? 0L;
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

        startMilliseconds = UserAircraft.Records[trimFrame].milliseconds;
        currentFrame = 0;
        CurrentFrameChanged?.Invoke(this, new(currentFrame));
        UserAircraft.Records = UserAircraft.Records.Skip(trimFrame).ToList();
        RecordsUpdated?.Invoke(this, new(null, startState?.AircraftTitle, UserAircraft.Records.Count));
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

        UserAircraft.Records = UserAircraft.Records.Take(trimFrame + 1).ToList();
        RecordsUpdated?.Invoke(this, new(null, startState?.AircraftTitle, UserAircraft.Records.Count));
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
            if (this.playUserAircraft)
                connector.Unfreeze(0);

            if (this.playAiArcraft)
            {
                foreach (var plane in AiAircraftList)
                {
                    if (plane.aiId != null)
                    {
                        connector.Unfreeze((uint)plane.aiId);
                    }
                }
            }
        }
    }

    /*
    public void NotifyPosition(AircraftPositionStruct? value)
    {
        currentPosition = value;
    }
    */

    public void FromData(string? fileName, SavedData data)
    {
        startMilliseconds = data.UserArcraft.StartTime;
        endMilliseconds = data.UserArcraft.EndTime;
        startState = data.UserArcraft.SimState == null ? null : SimState.ToStruct(data.UserArcraft.SimState);
        UserArcraftID = data.UserArcraft.UserArcraftID;


        //--- convert AI aircrafts
        AiAircraftList = new List<AiAircraft>();

        foreach (var aircraft in  data.AiAircraftList)
        {
            var mylist2 = new List<AiAircraftRecord>();
            if ((aircraft != null) && (aircraft.Records != null))
            {
                foreach (var record in aircraft.Records)
                {

                    if (record.Position != null)
                    {
                        mylist2.Add( new AiAircraftRecord { milliseconds = record.Time, position = (AiAircraftPositionStruct?)AiAircraftPosition.ToStruct(record.Position) });
                    }
                    else
                    {
                        mylist2.Add(new AiAircraftRecord { milliseconds = record.Time, position = null });
                    }
                }
            }
            SimStateStruct? aiState = aircraft.AircraftStatus == null ? null : SimState.ToStruct(aircraft.AircraftStatus);

            AiAircraft ai = new AiAircraft { AircraftStatus = aiState, Minutes = aircraft.Minutes, ObjectID = aircraft.objectID, StartIndex=aircraft.StartIndex, StopIndex = aircraft.StopIndex, Records = mylist2  };
            AiAircraftList.Add(ai);
        }

        Reset();

        //--- convert User aircraft

        var mylist = new List<AircraftRecord>();
        if (data.UserArcraft.Records != null) 
        {

            foreach (var record in data.UserArcraft.Records)
            {

                if (record.Position != null)
                {
                    mylist.Add(new AircraftRecord { milliseconds = record.Time, position = (AircraftPositionStruct?)AircraftPosition.ToStruct(record.Position) });
                }
                else
                {
                    mylist.Add(new AircraftRecord { milliseconds = record.Time, position = null });
                }
            }
        }

        SimStateStruct? userState = data.UserArcraft.SimState == null ? null : SimState.ToStruct(data.UserArcraft.SimState);

        UserAircraft = new UserAircraft { SimState = userState,  Records = mylist, UserArcraftID=data.UserArcraft.UserArcraftID, EndTime = data.UserArcraft.EndTime, StartTime = data.UserArcraft.StartTime};



        RecordsUpdated?.Invoke(this, new(fileName, data.UserArcraft.SimState?.AircraftTitle, data.UserArcraft.Records.Count));
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
        foreach (var plane in AiAircraftList)
        {
            if ((plane.aiRequestId != null) && (plane.aiRequestId == e.RequestId))
            {
                logger.LogDebug("Set AI ID {objectID}", e.ObjectId);
                plane .aiId= e.ObjectId;
            }
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
        if (this.playUserAircraft)
            connector.Freeze(0);


        var enumerator = UserAircraft.Records.GetEnumerator();
        currentFrame = -1;
        long? recordedElapsed = null;
        //AircraftPositionStruct? position = null;

        long? lastElapsed = 0;
        //AircraftPositionStruct? lastPosition = null;

        while (true)
        {
            // TODO: break this loop when window is closed

            // Wait for tick call from the sim frame
            tcs = new TaskCompletionSource<bool>();
            await tcs.Task;
            tcs = null;

            logger.LogTrace("RunReplay - after tick {currentFrame} {isReplayStopping} {forceReset} {recordedElapsed} {IsPausing}", currentFrame, isReplayStopping, forceReset, recordedElapsed, IsPausing);

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
                logger.LogTrace("RunReplay - Reset interaction. Pause frame {frame}.", pausedFrame);

                forceReset = false;

                enumerator = UserAircraft.Records.GetEnumerator();
                currentFrame = -1;
                recordedElapsed = null;
                //position = null;

                pausedFrame = null;
            }

            var currentElapsed = (long)((stopwatch.ElapsedMilliseconds - replayStartTime.Value) * rate);
            logger.LogTrace("RunReplay - new currentElapsed:{currentElapsed} - recordedElapsed: {recordedElapsed}", currentElapsed, recordedElapsed);

            try
            {
                while (!recordedElapsed.HasValue || currentElapsed > recordedElapsed)
                {
                    logger.LogTrace("RunReplay - Calculate recordedElapsed - Move next {currentElapsed}", currentElapsed);
                    var canMove = enumerator.MoveNext();

                    if (canMove)
                    {
                        currentFrame++;
                        AircraftRecord rec = enumerator.Current;
                        lastElapsed = recordedElapsed;
                        //lastPosition = position;
                        recordedElapsed = rec.milliseconds - startMilliseconds;
                        //position = recordedPosition;

                        // Try to check the velocity
                    }
                    else
                    {
                        // Last frame
                        logger.LogTrace("RunReplay - Last frame");
                        FinishReplay(true);
                        return;
                    }
                }
            }
            finally
            {
                logger.LogTrace("RunReplay - finally - Current Frame {currentFrame} {ellapsed}", currentFrame, currentElapsed);
                CurrentFrameChanged?.Invoke(this, new(currentFrame));
            }

            logger.LogTrace("RunReplay - before moving aircrafts {recordedElapsed} ", recordedElapsed);

            if (recordedElapsed.HasValue)
            {
                logger.LogTrace("RunReplay - moving aircrafts {currentFrame} ", currentFrame);
                if (this.playUserAircraft)
                    MoveAircraft((uint)UserAircraft.UserArcraftID, recordedElapsed.Value, UserAircraft.Records[currentFrame].position, null, null, 0);
                
                if (this.playAiArcraft)
                {
                    int i = 0;
                    foreach (var avion in AiAircraftList)
                    {
                        if (avion != null)
                        {
                            if ((avion.StartIndex <= currentFrame) && (avion.StopIndex >= currentFrame))
                                MoveAiAircraft((uint)avion.aiId, recordedElapsed.Value, avion.Records[currentFrame - avion.StartIndex].position, null, null, 0);

                            if (avion.StopIndex + 1 == currentFrame)
                            {
                                // would need to despawn aircraft when last index
                                MoveAiAircraft((uint)avion.aiId, recordedElapsed.Value, aidefaultPosition, null, null, 0);
                            }
                        }

                        ++i;
                    }
                }
            }

            logger.LogTrace("RunReplay - after moving aircrafts {currentFrame} ", currentFrame);
        }
    }

    private void FinishReplay(bool reachedLastFrame)
    {
        logger.LogInformation("RunReplay - Replay finished.");

        isReplayStopping = false;
        Unfreeze();
        
        if (this.playAiArcraft)
        {
            foreach (var avion in AiAircraftList)
            {
                if (avion.aiId != null)
                {
                    connector.Despawn((uint)avion.aiId);
                    avion.aiId = null;
                }
            }
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


        if (AiAircraftList != null)
        {
            foreach (var aircraft in AiAircraftList)
            {
                aircraft.aiId = null;
                aircraft.aiRequestId = null;
            }

        }

    }



    private void MoveAircraft(uint dwObjectId, long nextElapsed, AircraftPositionStruct? position, long? lastElapsed, AircraftPositionStruct? lastPosition, long currentElapsed)
    {
        logger.LogTrace("MoveAircraft - 1 Delta time {dwObjectId} {delta} {current} {recorded}.", dwObjectId, currentElapsed - nextElapsed, currentElapsed, nextElapsed);

        if (position == null)
            return;
            

        logger.LogTrace("MoveAircraft - 1b {dwObjectId}.", dwObjectId);

        var nextValue = AircraftPositionStructOperator.ToSet((AircraftPositionStruct)position);

        /*
        

        logger.LogTrace("MoveAircraft - 1a {dwObjectId}.", dwObjectId);

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
        */

        //logger.LogTrace("MoveAircraft - 2 {dwObjectId}.", dwObjectId);
        /*
        if ((dwObjectId != UserArcraftID) && currentPosition.HasValue && (lastTriggeredMilliseconds == null || stopwatch.ElapsedMilliseconds > lastTriggeredMilliseconds + EventThrottleMilliseconds))
        {
            lastTriggeredMilliseconds = stopwatch.ElapsedMilliseconds;
            connector.TriggerEvents(currentPosition.Value, (AircraftPositionStruct)position);
        }
        */
        

        connector.Set(dwObjectId, nextValue);
        logger.LogTrace("MoveAircraft - 3 {dwObjectId}.", dwObjectId);
    }



    private void MoveAiAircraft(uint dwObjectId, long nextElapsed, AiAircraftPositionStruct? position, long? lastElapsed, AiAircraftPositionStruct? lastPosition, long currentElapsed)
    {
        logger.LogTrace("MoveAircraft - 1 Delta time {dwObjectId} {delta} {current} {recorded}.", dwObjectId, currentElapsed - nextElapsed, currentElapsed, nextElapsed);

        if (position == null)
            return;


        logger.LogTrace("MoveAircraft - 1b {dwObjectId}.", dwObjectId);

        var nextValue = AiAircraftPositionStructOperator.ToSet((AiAircraftPositionStruct)position);

        /*
        var nextValue = AircraftPositionStructOperator.ToSet((AircraftPositionStruct)position);

        logger.LogTrace("MoveAircraft - 1a {dwObjectId}.", dwObjectId);

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
        */

        logger.LogTrace("MoveAircraft - 2 {dwObjectId}.", dwObjectId);
        /*
        if ((dwObjectId != UserArcraftID) && currentPosition.HasValue && (lastTriggeredMilliseconds == null || stopwatch.ElapsedMilliseconds > lastTriggeredMilliseconds + EventThrottleMilliseconds))
        {
            lastTriggeredMilliseconds = stopwatch.ElapsedMilliseconds;
            connector.TriggerAiEvents(currentPosition.Value, (AiAircraftPositionStruct)position);
        }
        */


        connector.Set(dwObjectId, nextValue);
        logger.LogTrace("MoveAircraft - 3 {dwObjectId}.", dwObjectId);
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
