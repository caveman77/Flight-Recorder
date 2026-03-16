using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FlightRecorder.Client.SimConnectMSFS;
using Microsoft.Extensions.Logging;
using SharpKml.Dom;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static FlightRecorder.Client.Logics.SavedData;


namespace FlightRecorder.Client.Logics;

public class RecorderLogic : IRecorderLogic, IDisposable
{
    public event EventHandler<RecordsUpdatedEventArgs>? RecordsUpdated;
    public event EventHandler<RecordsUpdatedEventArgs> AircraftUpdated;

    private readonly ILogger<RecorderLogic> logger;
    private readonly IConnector connector;
    private readonly Stopwatch stopwatch = new();

    private long? startMilliseconds;
    private long? endMilliseconds;
    private SimStateStruct startState;
    private List<(long milliseconds, uint dwObjectID, AircraftPositionStruct position)> records = new();
    private List<(long milliseconds, uint dwObjectID, SimStateStruct position)> sorrounding_records = new();

    // Match to the user aircraft
    private SimStateStruct simState;

    // User aircraft objectID
    uint userAircraftObjectID;

    private bool IsStarted => startMilliseconds.HasValue && records != null;
    private bool IsEnded => startMilliseconds.HasValue && endMilliseconds.HasValue;

    public RecorderLogic(ILogger<RecorderLogic> logger, IConnector connector)
    {
        logger.LogDebug("Creating instance of {class}", nameof(RecorderLogic));
        this.logger = logger;
        this.connector = connector;

        connector.SimStateUpdated += Connector_SimStateUpdated;
        connector.SorroundingAircraftUpdate += Connector_SimSouroundingAircraft;
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
            connector.SimStateUpdated -= Connector_SimStateUpdated;
            connector.SorroundingAircraftUpdate -= Connector_SimSouroundingAircraft;
        }
    }

    private void Connector_SimStateUpdated(object? sender, SimStateUpdatedEventArgs e)
    {
        simState = e.State;
        userAircraftObjectID = e.dwObjectID;
    }

    private void Connector_SimSouroundingAircraft(object? sender, SimStateUpdatedEventArgs e)
    {
        if (IsStarted && !IsEnded)
        {
            logger.LogDebug("RecorderLogic - aircraft: {AircraftNumber} {AircraftModel} {AircraftType} {AircraftTitle}", e.State.AircraftNumber, e.State.AircraftModel, e.State.AircraftType, e.State.AircraftTitle);
            sorrounding_records.Add((stopwatch.ElapsedMilliseconds, e.dwObjectID, e.State));

            AircraftUpdated?.Invoke(this, new(null, startState.AircraftTitle, sorrounding_records.Count));
        }

    }

    #region Public Functions

    public void Initialize()
    {
        logger.LogDebug("Initializing recorder...");

        stopwatch.Start();
    }

    public void Record()
    {
        logger.LogInformation("Start recording...");

        startMilliseconds = stopwatch.ElapsedMilliseconds;
        endMilliseconds = null;
        startState = simState;
        records = new List<(long milliseconds, uint dwObjectID, AircraftPositionStruct position)>();
        sorrounding_records = new List<(long milliseconds, uint dwObjectID, SimStateStruct position)>();
    }



    public void StopRecording()
    {
        if (endMilliseconds == null)
        {
            endMilliseconds = stopwatch.ElapsedMilliseconds;
            logger.LogDebug("Recording stopped. {totalFrames} frames recorded.", records.Count);
        }
    }

    public void NotifyPosition(uint dwObjectID, AircraftPositionStruct? value)
    {
        if (IsStarted && !IsEnded && value.HasValue)
        {
            records.Add((stopwatch.ElapsedMilliseconds, dwObjectID, value.Value));

            RecordsUpdated?.Invoke(this, new(null, startState.AircraftTitle, records.Count));
        }
    }

    
    public async Task<SavedData>  ToData(string clientVersion)
    {
        if (startMilliseconds == null) throw new InvalidOperationException("Cannot get data before started recording!");
        if (endMilliseconds == null) throw new InvalidOperationException("Cannot get data before finished recording!");

        // Find unic aircrafts
        List<AircraftWithObjectID> listAircraft = new List<AircraftWithObjectID>();
        foreach (var sor in sorrounding_records)
        {
            if (!listAircraft.Any(p => p.objectID == sor.dwObjectID))
            {
                listAircraft.Add(new AircraftWithObjectID { objectID = sor.dwObjectID, aircraftStatus = sor.position });
            }
        }


        var listPositionUserAircraft = records.Where(x => x.dwObjectID == userAircraftObjectID).Select(y => new AircraftRecord {  milliseconds = y.milliseconds, position = y.position }).ToList();
        var listStatusUserAircraft = sorrounding_records.Where(x => x.dwObjectID == userAircraftObjectID).Select(y => new AircraftStatus { milliseconds = y.milliseconds, position = y.position }).ToList();

        List<List<SavedRecord>> records_reorganised = new List<List<SavedRecord>>();
        List<List<AircraftStatus>> minutes_reorganised = new List<List<AircraftStatus>>();
        List<(int startindex, int stopindex)> Index_range = new List<(int startindex, int stopindex)>();


        foreach (var aircrafto in listAircraft)
        {
            uint aircraft = aircrafto.objectID;
            List<SavedRecord> singleAircraftRecord = new List<SavedRecord>();
            List<AircraftStatus> singleAircraftStatus = new List<AircraftStatus>();
            int newstartshift = -1;
            int newstopshift = - 1;

            if (aircraft == userAircraftObjectID)
            {
                newstartshift = 0;
                newstopshift = listPositionUserAircraft.Count -1;

                if ((listPositionUserAircraft.Count > 0) && (listStatusUserAircraft.Count > 0))
                {
                    for (int indexer = 0; indexer < listPositionUserAircraft.Count; indexer++)
                    {
                        
                        long Time = listPositionUserAircraft[indexer].milliseconds;
                        AircraftPosition? Position = null;

                        if (listPositionUserAircraft[indexer].position == null)
                        {
                            Position = null;
                        }
                        else
                        {
                            Position = AircraftPosition.FromStruct((AircraftPositionStruct)listPositionUserAircraft[indexer].position);
                        }

                        SavedRecord record2 = new SavedRecord(Time, Position);
                        singleAircraftRecord.Add(record2);
                    }

                    

                    //singleAircraftRecord = listPositionUserAircraft.Any() ? listPositionUserAircraft : new List<AircraftRecord>();
                    singleAircraftStatus = listStatusUserAircraft.Any() ? listStatusUserAircraft : new List<AircraftStatus>();

                }
                else
                {
                    logger.LogError("User aircraft found empty !");
                }
            }
            else
            {
                // Manage frames
                var listPositionAIAircraft = records.Where(x => x.dwObjectID == aircraft).Select(y => new AircraftRecord { milliseconds = y.milliseconds, position = y.position }).ToList();

                var firsthappearance = listPositionAIAircraft[0].milliseconds;

                // We start with the user airfact
                //singleAircraftRecord = listPositionUserAircraft.Any() ? listPositionUserAircraft : new List<AircraftRecord>();

                newstartshift = listPositionUserAircraft.FindIndex(o => o.milliseconds >= firsthappearance);
                newstopshift = newstartshift + listPositionAIAircraft.Count - 1;


                // Taking the assumption that we receive all frames for all aircrafts, the AI one is just a shift in time
                // 
                double last_longitude = 0;
                double last_latitude = 0;

                for (int indexer=0; indexer< listPositionAIAircraft.Count;indexer++)
                {
                    
                    long Time = listPositionUserAircraft[indexer + newstartshift].milliseconds;
                    AircraftPosition? Position = null;

                    //logger.LogError("tt {indexer} {startshift} {Count} {Count2}", indexer, startshift, listPositionAIAircraft.Count, singleAircraftRecord.Count);
                    //singleAircraftRecord[indexer].position = listPositionAIAircraft[indexer + startshift].position;
                    if ((indexer!=0) || (listPositionAIAircraft[indexer].position.Value.Latitude != last_latitude) || (listPositionAIAircraft[indexer].position.Value.Longitude != last_longitude))
                        Position = AircraftPosition.FromStruct((AircraftPositionStruct)listPositionAIAircraft[indexer].position);
                    else
                        Position = null;

                        // To be done compare with previous value of AircraftPositionStruct and set to null

                    SavedRecord record2 = new SavedRecord(Time, Position);
                    singleAircraftRecord.Add(record2);
                }

                // Manage minute status
                var listPositionAIStatus = sorrounding_records.Where(x => x.dwObjectID == aircraft).Select(y => new AircraftStatus { milliseconds = y.milliseconds, position = y.position }).ToList();
                var matching_minute_not_null = new AircraftStatus();

                foreach (var userStatus in listStatusUserAircraft)
                { 
                        var matching_status = new AircraftStatus();
                        try
                        {
                            matching_status = listPositionAIStatus.Where(x => x.milliseconds >= userStatus.milliseconds - 1000 && x.milliseconds <= userStatus.milliseconds + 1000).OrderBy(x => x.milliseconds).First();

                        }
                        catch (ArgumentNullException)
                        {
                            matching_status.position = null;
                        }
                        catch (System.InvalidOperationException)
                        {
                            matching_status.position = null;
                        }

                        // Overload the AI aircraft value to be sure that user Aircraft and AI are aligned
                        matching_status.milliseconds = userStatus.milliseconds;
                        singleAircraftStatus.Add(matching_status);

                }

                

            }

            Index_range.Add((newstartshift, newstopshift));

            records_reorganised.Add(singleAircraftRecord);
            minutes_reorganised.Add(singleAircraftStatus);

        }

        await Task.Yield();

        SavedData outcome = new SavedData(clientVersion, startMilliseconds.Value, endMilliseconds.Value, startState, userAircraftObjectID, listAircraft, records_reorganised, minutes_reorganised, Index_range);
        return outcome;
    }
    
    #endregion
}
