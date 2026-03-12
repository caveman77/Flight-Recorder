using FlightRecorder.Client.SimConnectMSFS;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

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

    class simpleDTO
    {
        public long milliseconds;
        public AircraftPositionStruct position;
    }

    public void StopRecording()
    {
        if (endMilliseconds == null)
        {
            endMilliseconds = stopwatch.ElapsedMilliseconds;
            logger.LogDebug("Recording stopped. {totalFrames} frames recorded.", records.Count);

            // var listAircraft = sorrounding_records.GroupBy( x => new { x.dwObjectID }).Select(l => l.).ToList();
            var listAircraft = sorrounding_records.Select(y => y.dwObjectID).Distinct().ToList();

            

            var listPositionUserAircraft = records.Where( x => x.dwObjectID == userAircraftObjectID ).Select(y => new simpleDTO { milliseconds = y.milliseconds, position = y.position}).ToList();
            foreach (var aircraft in listAircraft)
            {
                var singleAircraftRecord = new List<simpleDTO>();

                if (aircraft == userAircraftObjectID)
                {
                    if (listPositionUserAircraft.Count > 0)
                    {
                        singleAircraftRecord = listPositionUserAircraft.Any() ? listPositionUserAircraft : new List<simpleDTO>();
                    }
                }
                else
                {
                    var listPositionAIAircraft = records.Where(x => x.dwObjectID == aircraft).Select(y => new simpleDTO { milliseconds = y.milliseconds, position = y.position }).ToList();
                    var matching_record_not_null = new simpleDTO();

                    foreach (var item in listPositionUserAircraft)
                    {
                        var matching_record = new simpleDTO();
                        try
                        {
                            matching_record = listPositionAIAircraft.Where(x => x.milliseconds >= item.milliseconds).OrderBy(x => x.milliseconds).First();

                            if (matching_record != null)
                            {
                                singleAircraftRecord.Add(matching_record);
                                matching_record_not_null = matching_record;
                            }
                            else
                            {
                                // to be managed better by removing the aircraft
                                singleAircraftRecord.Add(matching_record_not_null);
                            }


                        }
                        catch (ArgumentNullException ex)
                        {
                            matching_record = matching_record_not_null;
                        }
                        catch (System.InvalidOperationException  ex)
                        {
                            matching_record = matching_record_not_null;
                        }
                        
                        
                    }

                }

            }
            


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
        return new(clientVersion, startMilliseconds.Value, endMilliseconds.Value, startState, records);
    }
    
    #endregion
}
