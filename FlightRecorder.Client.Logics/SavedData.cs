using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace FlightRecorder.Client.Logics;

// Store an aircraft frame
public class AircraftRecord
{
    public long milliseconds;
    public AircraftPositionStruct position;
}


// Store the event send every minutes telling if any aircraft is here
public class AircraftStatus
{
    public long milliseconds;
    public SimStateStruct? position;

}

public class AircraftWithObjectID
{
    public uint objectID;
    public required SimStateStruct aircraftStatus;
}

public class SavedData
{

    // Aircrafts are order the same way on the different lists
    public SavedData(string clientVersion, long startTime, long endTime, SimStateStruct? simState, uint userArcraftID, List<AircraftWithObjectID> aircraftList, List<List<AircraftRecord>> records_reorganised, List<List<AircraftStatus>> minutes_reoganised )
    {
        ClientVersion = clientVersion;
        StartTime = startTime;
        EndTime = endTime;
        StartState = simState.HasValue ? SimState.FromStruct(simState.Value) : null;

        AircraftList = new List<AircraftWithObjectID>();
        Records = new List<List<SavedRecord>>();
        Minute_status = new List<List<AircraftStatus>>();
        UserArcraftID = userArcraftID;

        int i = 0;
        foreach (List<AircraftRecord> aircraftListrec in records_reorganised)
        { 
            
            if (aircraftListrec.Count > 0)
            {
                Records.Add( aircraftListrec.Select(r => new SavedRecord
                    (
                        r.milliseconds,
                        AircraftPosition.FromStruct(r.position)
                    )).ToList());

                Minute_status.Add(minutes_reoganised[i]);
                AircraftList.Add(aircraftList[i]);
            }

            i++;

        }
    }

    [JsonConstructor]
    public SavedData(string clientVersion, long startTime, long endTime, SimState? startState, uint userArcraftID, List<AircraftWithObjectID> aircraftList, List<List<SavedRecord>>? records, List<List<AircraftStatus>>? minute_status)
    {
        ClientVersion = clientVersion;
        StartTime = startTime;
        EndTime = endTime;
        StartState = startState;
        Records = records ?? new List<List<SavedRecord>>();
        AircraftList = aircraftList;
        Minute_status = minute_status;
        UserArcraftID = userArcraftID;
    }

    public string ClientVersion { get; set; }
    public long StartTime { get; set; }
    public long EndTime { get; set; }
    public SimState? StartState { get; set; }

    public uint UserArcraftID { get; set; }

    public List<AircraftWithObjectID> AircraftList { get; set; }
    public List<List<SavedRecord>>? Records { get; set; }

    public List<List<AircraftStatus>>? Minute_status { get; set; }

    public class SavedRecord
    {
        public SavedRecord(long time, AircraftPosition position)
        {
            Time = time;
            Position = position;
        }

        public long Time { get; set; }
        public AircraftPosition Position { get; set; }
    }
}
