using System;
using System.Threading.Tasks;
using FlightRecorder.Client.SimConnectMSFS;

namespace FlightRecorder.Client.Logics
{
    public interface IRecorderLogic
    {
        event EventHandler<RecordsUpdatedEventArgs> RecordsUpdated;
        event EventHandler<RecordsUpdatedEventArgs> AircraftUpdated;

        void Initialize();
        void Record();
        void StopRecording();
        void NotifyPosition(uint dwObjectID, AircraftPositionStruct? value);

        void NotifyAiPosition(uint dwObjectID, AiAircraftPositionStruct? value);


        Task <SavedData> ToData(string clientVersion);
    }
}