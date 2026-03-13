using FlightRecorder.Client.SimConnectMSFS;
using Microsoft.Extensions.Logging;
using SharpKml.Dom;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using static FlightRecorder.Client.Logics.SavedData;


namespace FlightRecorder.Client.Logics;

public class RecorderLogic : IRecorderLogic, IDisposable
{
    public event EventHandler<RecordsUpdatedEventArgs>? RecordsUpdated;

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
            logger.LogError("RecorderLogic - Connector_SimSouroundingAircraft CHU CHU_LISTAIRCRAFT - aircraft: {AircraftNumber} {AircraftModel} {AircraftType} {AircraftTitle}", e.State.AircraftNumber, e.State.AircraftModel, e.State.AircraftType, e.State.AircraftTitle);
            sorrounding_records.Add((stopwatch.ElapsedMilliseconds, e.dwObjectID, e.State));
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

            // To be done: Filtering here to send information about the user aircraft
            RecordsUpdated?.Invoke(this, new(null, startState.AircraftTitle, records.Count));
        }
    }

    
    // A lot of work here to be done
    public SavedData ToData(string clientVersion)
    {
        if (startMilliseconds == null) throw new InvalidOperationException("Cannot get data before started recording!");
        if (endMilliseconds == null) throw new InvalidOperationException("Cannot get data before finished recording!");

        List<AircraftWithObjectID> listAircraft = sorrounding_records.Select(y => new AircraftWithObjectID { objectID = y.dwObjectID,  aircraftStatus = y.position }).Distinct().ToList();
        var listPositionUserAircraft = records.Where(x => x.dwObjectID == userAircraftObjectID).Select(y => new AircraftRecord {  milliseconds = y.milliseconds, position = y.position }).ToList();
        var listStatusUserAircraft = sorrounding_records.Where(x => x.dwObjectID == userAircraftObjectID).Select(y => new AircraftStatus { milliseconds = y.milliseconds, position = y.position }).ToList();

        List<List<AircraftRecord>> records_reorganised = new List<List<AircraftRecord>>();
        List<List<AircraftStatus>> minutes_reorganised = new List<List<AircraftStatus>>();

        foreach (var aircrafto in listAircraft)
        {
            uint aircraft = aircrafto.objectID;
            List<AircraftRecord> singleAircraftRecord = new List<AircraftRecord>();
            List<AircraftStatus> singleAircraftStatus = new List<AircraftStatus>();

            if (aircraft == userAircraftObjectID)
            {
                if ((listPositionUserAircraft.Count > 0) && (listStatusUserAircraft.Count > 0))
                {
                    singleAircraftRecord = listPositionUserAircraft.Any() ? listPositionUserAircraft : new List<AircraftRecord>();
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
                var matching_record_not_null = new AircraftRecord();

                foreach (var item in listPositionUserAircraft)
                {
                    var matching_record = new AircraftRecord();
                    try
                    {
                        matching_record = listPositionAIAircraft.Where(x => x.milliseconds >= item.milliseconds).OrderBy(x => x.milliseconds).First();

                    }
                    catch (ArgumentNullException)
                    {
                        matching_record = matching_record_not_null;
                    }
                    catch (System.InvalidOperationException)
                    {
                        matching_record = matching_record_not_null;
                    }

                    if (matching_record != null)
                    {
                        matching_record_not_null = matching_record;
                    }
                    else
                    {
                        // to be managed better by removing the aircraft
                        matching_record = matching_record_not_null;
                    }

                    matching_record.milliseconds = item.milliseconds;
                    singleAircraftRecord.Add(matching_record);
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

            records_reorganised.Add(singleAircraftRecord);
            minutes_reorganised.Add(singleAircraftStatus);

        }

        return new(clientVersion, startMilliseconds.Value, endMilliseconds.Value, startState, userAircraftObjectID, listAircraft, records_reorganised, minutes_reorganised);
    }
    
    #endregion
}
