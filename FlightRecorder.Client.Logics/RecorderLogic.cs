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
    private List<(long milliseconds, uint dwObjectID, AiAircraftPositionStruct position)> airecords = new();
    private List<(long milliseconds, uint dwObjectID, SimStateStruct position)> sorrounding_records = new();

    // Match to the user aircraft
    private SimStateStruct simState;

    // User aircraft objectID
    uint? userAircraftObjectID = null;

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
            if ((userAircraftObjectID != null) && (userAircraftObjectID != e.dwObjectID))
            {
                sorrounding_records.Add((stopwatch.ElapsedMilliseconds, e.dwObjectID, e.State));

                AircraftUpdated?.Invoke(this, new(null, startState.AircraftTitle, sorrounding_records.Count));
            }
        }

    }

    #region Public Functions

    public void Initialize()
    {
        logger.LogDebug("Initializing recorder...");
        userAircraftObjectID = null;

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
            userAircraftObjectID = dwObjectID;

            records.Add((stopwatch.ElapsedMilliseconds, dwObjectID, value.Value));

            RecordsUpdated?.Invoke(this, new(null, startState.AircraftTitle, records.Count));
        }
    }

    public void NotifyAiPosition(uint dwObjectID, AiAircraftPositionStruct? value)
    {

        if (IsStarted && !IsEnded && value.HasValue && (userAircraftObjectID != null) && (dwObjectID != userAircraftObjectID))
        {
                airecords.Add((stopwatch.ElapsedMilliseconds, dwObjectID, value.Value));

            }
        }


    public async Task<SavedData>  ToData(string clientVersion)
    {
        if (startMilliseconds == null) throw new InvalidOperationException("Cannot get data before started recording!");
        if (endMilliseconds == null) throw new InvalidOperationException("Cannot get data before finished recording!");

        // Find unic aircrafts
        List<uint> listAircraft = new List<uint>();
        foreach (var sor in sorrounding_records)
        {
            if (!listAircraft.Any(p => p == sor.dwObjectID))
            {
                listAircraft.Add(sor.dwObjectID);
            }
        }


        var listPositionUserAircraft = records.Where(x => x.dwObjectID == userAircraftObjectID).Select(y => new AircraftRecord {  milliseconds = y.milliseconds, position = y.position }).ToList();
        var listStatusUserAircraft = sorrounding_records.Where(x => x.dwObjectID == userAircraftObjectID).Select(y => new AircraftStatus ( y.milliseconds, y.position )).ToList();

        List<List<SavedRecord>> records_reorganised = new List<List<SavedRecord>>();
        List<List<AircraftStatus>> minutes_reorganised = new List<List<AircraftStatus>>();
        List<(int startindex, int stopindex)> Index_range = new List<(int startindex, int stopindex)>();

        UserAircraftForSave userAircraft = new UserAircraftForSave();
        List<AiAircraftForSave> aiArcraftList = new List<AiAircraftForSave>();


        //------------ User aircraft conversion
        if (userAircraftObjectID != null)
        {
            List<AircraftStatus> singleAircraftStatus = new List<AircraftStatus>();
            List<SavedRecord> singleAircraftRecord = new List<SavedRecord>();

            if (listPositionUserAircraft.Count > 0)
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


                SimState StartState = SimState.FromStruct(simState);

                userAircraft = new UserAircraftForSave
                {
                    Records = singleAircraftRecord,
                    EndTime = endMilliseconds.Value,
                    StartTime = startMilliseconds.Value,
                    UserArcraftID = (uint)userAircraftObjectID,
                    SimState = StartState
                };
            }
            else
            {
                logger.LogError("User aircraft found empty !");
            }

        }


        foreach (var aircraft in listAircraft)
        {
            
            List<AircraftStatus> singleAircraftStatus = new List<AircraftStatus>();
            int newstartshift = -1;
            int newstopshift = - 1;


            //------------ AI aircraft conversion
            {
                List<AiSavedRecord> singleAircraftRecord = new List<AiSavedRecord>();
                AiAircraftForSave aiAiracraft = new AiAircraftForSave();
                aiAiracraft.objectID = aircraft;

                // Manage frames
                var listPositionAIAircraft = airecords.Where(x => x.dwObjectID == aircraft).Select(y => new AiAircraftRecord { milliseconds = y.milliseconds, position = y.position }).ToList();

                var firsthappearance = listPositionAIAircraft[0].milliseconds;

                // We start with the user airfact
                //singleAircraftRecord = listPositionUserAircraft.Any() ? listPositionUserAircraft : new List<AircraftRecord>();

                newstartshift = listPositionUserAircraft.FindIndex(o => o.milliseconds >= firsthappearance);
                newstopshift = newstartshift + listPositionAIAircraft.Count - 1;


                // Taking the assumption that we receive all frames for all aircrafts, the AI one is just a shift in time
                // 
                double last_longitude = 0;
                double last_latitude = 0;

                for (int indexer=0; ((indexer< listPositionAIAircraft.Count) && ((indexer + newstartshift )< listPositionUserAircraft.Count)) ; indexer++)
                {
                    
                    long Time = listPositionUserAircraft[indexer + newstartshift].milliseconds;
                    AiAircraftPosition? Position = null;

                    //logger.LogError("tt {indexer} {startshift} {Count} {Count2}", indexer, startshift, listPositionAIAircraft.Count, singleAircraftRecord.Count);
                    //singleAircraftRecord[indexer].position = listPositionAIAircraft[indexer + startshift].position;
                    if ((indexer!=0) || (listPositionAIAircraft[indexer].position.Value.Latitude != last_latitude) || (listPositionAIAircraft[indexer].position.Value.Longitude != last_longitude))
                        Position = AiAircraftPosition.FromStruct(manageLights((AiAircraftPositionStruct)listPositionAIAircraft[indexer].position));
                    else
                        Position = null;

                    // To be done compare with previous value of AircraftPositionStruct and set to null

                    AiSavedRecord record2 = new AiSavedRecord(Time, Position);
                    singleAircraftRecord.Add(record2);

                }

                // Manage minute status
                var listPositionAIStatus = sorrounding_records.Where(x => x.dwObjectID == aircraft).Select(y => new AircraftStatus ( y.milliseconds, y.position )).ToList();
                var matching_minute_not_null = new AircraftStatus();

                foreach (var userStatus in listPositionAIStatus)
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

                if (singleAircraftStatus.Count > 0)
                    aiAiracraft.AircraftStatus = singleAircraftStatus[0].position ==null ? null : SimState.FromStruct((SimStateStruct)singleAircraftStatus[0].position);

                aiAiracraft.Records = singleAircraftRecord;
                aiAiracraft.Minutes = singleAircraftStatus;

                aiAiracraft.StartIndex = newstartshift;
                aiAiracraft.StopIndex = newstopshift;

                aiArcraftList.Add(aiAiracraft);

            }

        }

        await Task.Yield();

        SavedData outcome = new SavedData(clientVersion, userAircraft, aiArcraftList);
        return outcome;
    }

    private AiAircraftPositionStruct manageLights(AiAircraftPositionStruct position)
    {
        AiAircraftPositionStruct result = position;

        result.LightLogo = 1;
        result.LightNav = 1;

        if ((position.GroundSpeed > 1) || (position.GeneralEngineCombustion1 == 1))
            result.LightBeacon = 1;
        else
            result.LightBeacon = 0;

        // Landing or TO
        if (position.IsOnRunway == 1)
        {
            result.LightLanding = 1;
            result.LightStrobe = 1;
            result.LightLogo = 1;
            result.LightTaxi = 0;
            

            return result;
        }
        
        // TAXI
        if ((position.IsOnGround == 1) && (position.GeneralEngineCombustion1 == 1))
        {
            result.LightLanding = 0;
            result.LightStrobe = 0;
            result.LightLogo = 1;
            result.LightTaxi = 1;

            return result;
        }

        // At Stand 
        if ((position.IsOnGround == 1) && (position.GeneralEngineCombustion1 == 0))
        {
            result.LightLanding = 0;
            result.LightStrobe = 0;
            result.LightLogo = 1;
            result.LightTaxi = 0;

            return result;
        }

        // In air
        result.LightLanding = 0;
        result.LightStrobe = 1;
        result.LightLogo = 1;
        result.LightTaxi = 0;

        if ( position.AltitudeAboveGround < 10000)
        {
            result.LightLanding = 1;
        }

        return result;

    }
    #endregion
}
