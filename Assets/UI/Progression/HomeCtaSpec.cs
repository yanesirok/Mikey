namespace Mikey.UI.Progression
{
    /// <summary>Label + destination screen id for Home's single dynamic primary CTA.</summary>
    public readonly struct HomeCtaSpec
    {
        public string Label { get; }
        public string Destination { get; }

        public HomeCtaSpec(string label, string destination)
        {
            Label = label;
            Destination = destination;
        }
    }
}
