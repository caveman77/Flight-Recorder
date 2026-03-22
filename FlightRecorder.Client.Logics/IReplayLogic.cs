using System;
using System.Collections.Generic;

namespace FlightRecorder.Client.Logics;

public interface IReplayLogic
{
    event EventHandler<RecordsUpdatedEventArgs> RecordsUpdated;
    event EventHandler ReplayFinished;
    event EventHandler<CurrentFrameChangedEventArgs> CurrentFrameChanged;

    public UserAircraft UserAircraft { get; }
    /*
    public List<List<(long milliseconds, AircraftPositionStruct? position)>> Records { get; }

    public List<(long milliseconds, AircraftPositionStruct position)> User_Records { get; }
    */
    bool IsReplayable { get; }

    public void SetReplayScope(bool playUserAircraft, bool playAiArcrafts);
    public bool Replay();
    bool PauseReplay();
    bool ResumeReplay();
    void Seek(int value);
    void TrimStart();
    void TrimEnd();
    bool StopReplay();
    void ChangeRate(double rate);
    void SetRepeat(bool repeat);
    void Unfreeze();
    //void NotifyPosition(AircraftPositionStruct? value);

    void FromData(string? fileName, SavedData data);
    //SavedData ToData(string clientVersion);
}
