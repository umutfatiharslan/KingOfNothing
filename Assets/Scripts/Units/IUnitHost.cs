namespace KingOfNothing.Units
{
    /// <summary>Something a unit can be inside of or standing on (buildings, the top of a tower).</summary>
    public interface IUnitHost
    {
        string HostName { get; }

        /// <summary>Lets the unit out (at the door / the foot of the tower). Called when the unit walks away.</summary>
        void Release(Unit unit);
    }
}
