namespace caveman.core.entities
{

    // 1. Return Object with Green Metrics
    public class CompressionResult
    {
        public string CompressedText { get; set; } = string.Empty;
        public int OriginalTokens { get; set; }
        public int CompressedTokens { get; set; }

        // Derived metrics
        public double SavedTokens => Math.Max(0, OriginalTokens - CompressedTokens);
        public double EfficiencyPercentage => OriginalTokens == 0 ? 0 : (SavedTokens / OriginalTokens) * 100;

        // Energy Metric: estimated 5 mWh per saved token
        public double EstimatedEnergySavedMWh => SavedTokens * 0.005;

        // CO2 Metric: global estimate (~0.4 mg CO2 per saved mWh)
        public double EstimatedCO2SavedMg => EstimatedEnergySavedMWh * 0.4;
    }
}
