namespace ServerPlugin.ShipFixer;

public enum ShipFixerResult
{
    OK,
    TooFewGrids,
    TooManyGrids,
    UnknownProblem,
    OwnedByDifferentPlayer,
    DifferentOwnerOnConnectedGrid,
    GridOccupied,
    ShipFixed,
    GridNotFound,
    PlayersWereEjected,
    GridMoving,
}
