namespace Mikey.UI.Navigation
{
    /// <summary>
    /// Canonical route table for Mikey's screens. Panels need no entry; only heavy
    /// scene-backed screens are registered. Scene screens are added here as they are
    /// migrated out of the shared document into additive scenes.
    /// </summary>
    public static class MikeyScreens
    {
        public static ScreenRouteTable BuildDefault()
        {
            var table = new ScreenRouteTable();
            // Scene-backed screens are registered here during migration (see plan Task 7).
            return table;
        }
    }
}
