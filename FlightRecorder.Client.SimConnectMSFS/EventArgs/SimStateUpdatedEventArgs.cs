using System;

namespace FlightRecorder.Client.SimConnectMSFS
{
    public class SimStateUpdatedEventArgs : EventArgs
    {
        public SimStateUpdatedEventArgs(uint dwObjectID, SimStateStruct state)
        {
            State = state;
            this.dwObjectID = dwObjectID;
        }

        public SimStateStruct State { get; }
        public uint dwObjectID { get; }
    }
}
