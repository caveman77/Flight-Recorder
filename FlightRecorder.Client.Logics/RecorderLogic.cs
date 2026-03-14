using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using FlightRecorder.Client.SimConnectMSFS;
using Microsoft.Extensions.Logging;
using SharpKml.Dom;
using static System.Runtime.InteropServices.JavaScript.JSType;
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
    
    // 1st dimension is the time (frame), the second is the aircraft
    private List<List<SavedRecord>> records = new List<List<SavedRecord>>();

    // Current row to update (matching to frame)
    private int CurrentRowUnderRecording = -1;

    //private List<(long milliseconds, uint dwObjectID, AircraftPositionStruct position)> records = new();
    private List<(long milliseconds, uint dwObjectID, SimStateStruct position)> sorrounding_records = new();

    // Match to the user aircraft
    private SimStateStruct simState;

    // User aircraft objectID. Will be updated quite fast as we listen even if the recording is not launched
    uint? userAircraftObjectID  = null;

    // User aircraft index
    private uint? userAircraftIndex = null;

    private bool IsStarted => startMilliseconds.HasValue && records != null;
    private bool IsEnded => startMilliseconds.HasValue && endMilliseconds.HasValue;

    // Giving DwObjectID to Internal index relationship
    private Dictionary<uint, uint> DwObjectId2InternalIndex = new();
    
    // Current Number of collected Aircraft
    private uint CurrentNbOfAircraftCollected = 0;

    // Maximum number of collected aircrafts
    uint MaxNumberOfCollectedAircraft = 3;

    // Last index of the collected aircraft
    private int LastIndexOfCollectedAircraft = -1;

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
        //records = new List<(long milliseconds, uint dwObjectID, AircraftPositionStruct position)>();
        records = new List<List<SavedRecord>>();

        sorrounding_records = new List<(long milliseconds, uint dwObjectID, SimStateStruct position)>();
        CurrentNbOfAircraftCollected = 0;
        DwObjectId2InternalIndex = new();
        CurrentRowUnderRecording = -1;
        LastIndexOfCollectedAircraft = -1;
    }



    public void StopRecording()
    {
        if (endMilliseconds == null)
        {
            endMilliseconds = stopwatch.ElapsedMilliseconds;
            logger.LogDebug("Recording stopped. {totalFrames} frames recorded.", records.Count);
        }
    }

    // Return Null if the maximum number of aircraft has been reached
    private uint? GetAircraftIndex(uint dwObjectID)
    {
        uint? ourIndex = 0;

        try
        {
            ourIndex = DwObjectId2InternalIndex[dwObjectID];
        }
        catch (KeyNotFoundException)
        {
            if (DwObjectId2InternalIndex.Count < MaxNumberOfCollectedAircraft)
            {
                LastIndexOfCollectedAircraft++;
                ourIndex = (uint)LastIndexOfCollectedAircraft;
                DwObjectId2InternalIndex.Add(dwObjectID, (uint)ourIndex);

                if (dwObjectID == userAircraftObjectID)
                    userAircraftIndex = ourIndex;
            }
            else
                ourIndex = null;
        }

        return ourIndex;
    }


    public void NotifyPosition(uint dwObjectID, AircraftPositionStruct? value)
    {
        if (IsStarted && !IsEnded && value.HasValue)
        {
            var ourIndex = GetAircraftIndex((uint)dwObjectID);

            if (ourIndex != null)
            {
                if (dwObjectID == userAircraftObjectID)
                {
                    var ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    SavedRecord UserPlane = new SavedRecord(ElapsedMilliseconds, AircraftPosition.FromStruct((AircraftPositionStruct)value));
                    SavedRecord Others = new SavedRecord(ElapsedMilliseconds, null);

                    // Pre-create all aircraft with null position value
                    List<SavedRecord> list2beAdded = Enumerable.Repeat(Others, (int)MaxNumberOfCollectedAircraft).ToList();
                    list2beAdded[(int)ourIndex] = UserPlane;

                    records.Add(list2beAdded);
                    CurrentRowUnderRecording++;

                    RecordsUpdated?.Invoke(this, new(null, startState.AircraftTitle, records.Count));

                }
                else
                {
                    records[CurrentRowUnderRecording][(int)ourIndex].Position = AircraftPosition.FromStruct((AircraftPositionStruct)value);
                }
               
            }

        }
    }

    
    // A lot of work here to be done
    public SavedData ToData(string clientVersion)
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



        return new(clientVersion, startMilliseconds.Value, endMilliseconds.Value, startState, (uint)userAircraftObjectID, listAircraft, records);
    }
    
    #endregion
}
