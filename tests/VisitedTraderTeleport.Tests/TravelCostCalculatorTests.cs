using System;
using System.Collections.Generic;
using VisitedTraderTeleport;
using Xunit;

namespace VisitedTraderTeleport.Tests;

public class TravelCostCalculatorTests
{
    private sealed class FakeLocalizationProvider : ILocalizationProvider
    {
        private readonly Dictionary<string, string> translations;

        public FakeLocalizationProvider(Dictionary<string, string> translations = null)
        {
            this.translations = translations ?? new Dictionary<string, string>();
        }

        public string Get(string key) => translations.TryGetValue(key, out string value) ? value : key;

        public string Format(string key, params object[] args) => string.Format(Get(key), args);
    }

    private sealed class FakePlayerInventory : IPlayerInventory
    {
        private readonly Dictionary<string, int> counts;
        private readonly HashSet<string> existingItems;
        private readonly Func<string, int, int> removeItemHandler;

        public FakePlayerInventory(
            Dictionary<string, int> counts,
            HashSet<string> existingItems = null,
            Func<string, int, int> removeItemHandler = null)
        {
            this.counts = counts;
            this.existingItems = existingItems ?? new HashSet<string>(counts.Keys);
            this.removeItemHandler = removeItemHandler;
        }

        public bool ItemExists(string itemName) => existingItems.Contains(itemName);

        public int CountItem(string itemName) => counts.TryGetValue(itemName, out int count) ? count : 0;

        public int RemoveItem(string itemName, int count)
        {
            int removed = removeItemHandler != null ? removeItemHandler(itemName, count) : Math.Min(count, CountItem(itemName));
            counts[itemName] = Math.Max(0, CountItem(itemName) - removed);
            return removed;
        }
    }

    [Fact]
    public void CalculateCost_DisabledSettings_ReturnsZero()
    {
        var settings = new TravelCostSettings { Enabled = false, PerMeter = 1f };

        int cost = TravelCostCalculator.CalculateCost(100f, settings);

        Assert.Equal(0, cost);
    }

    [Fact]
    public void CalculateCost_ZeroOrNegativePerMeter_ReturnsZero()
    {
        var settings = new TravelCostSettings { Enabled = true, PerMeter = 0f };

        int cost = TravelCostCalculator.CalculateCost(100f, settings);

        Assert.Equal(0, cost);
    }

    [Fact]
    public void CalculateCost_AppliesPerMeterRate()
    {
        var settings = new TravelCostSettings { Enabled = true, PerMeter = 0.5f, Minimum = 0 };

        int cost = TravelCostCalculator.CalculateCost(100f, settings);

        Assert.Equal(50, cost);
    }

    [Fact]
    public void CalculateCost_ClampsToMinimum()
    {
        var settings = new TravelCostSettings { Enabled = true, PerMeter = 0.1f, Minimum = 20 };

        int cost = TravelCostCalculator.CalculateCost(10f, settings);

        Assert.Equal(20, cost);
    }

    [Fact]
    public void CalculateCost_ClampsToMaxCalculatedCost()
    {
        var settings = new TravelCostSettings { Enabled = true, PerMeter = 1000f, Minimum = 0 };

        int cost = TravelCostCalculator.CalculateCost(100000f, settings);

        Assert.Equal(1000000, cost);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void CalculateCost_NonFiniteDistance_ClampsToMax(float distance)
    {
        var settings = new TravelCostSettings { Enabled = true, PerMeter = 1f, Minimum = 0 };

        int cost = TravelCostCalculator.CalculateCost(distance, settings);

        Assert.Equal(1000000, cost);
    }

    [Fact]
    public void GetDisplayRate_PerMeterAtLeastOne_ReturnsCeilingAmountAndOneMeter()
    {
        TravelCostCalculator.GetDisplayRate(2.3f, out int amount, out int meters);

        Assert.Equal(3, amount);
        Assert.Equal(1, meters);
    }

    [Fact]
    public void GetDisplayRate_PerMeterLessThanOne_ReturnsOneAndRoundedMeters()
    {
        TravelCostCalculator.GetDisplayRate(0.1f, out int amount, out int meters);

        Assert.Equal(1, amount);
        Assert.Equal(10, meters);
    }

    [Fact]
    public void HasSufficientItems_CostZeroOrLess_ReturnsTrue()
    {
        var inventory = new FakePlayerInventory(new Dictionary<string, int>());

        bool result = TravelCostCalculator.HasSufficientItems(inventory, "ammoGasCan", 0, out int available);

        Assert.True(result);
        Assert.Equal(0, available);
    }

    [Fact]
    public void HasSufficientItems_ItemDoesNotExist_ReturnsFalseWithZeroAvailable()
    {
        var inventory = new FakePlayerInventory(new Dictionary<string, int>(), existingItems: new HashSet<string>());

        bool result = TravelCostCalculator.HasSufficientItems(inventory, "ammoGasCan", 5, out int available);

        Assert.False(result);
        Assert.Equal(0, available);
    }

    [Fact]
    public void HasSufficientItems_EnoughItems_ReturnsTrue()
    {
        var inventory = new FakePlayerInventory(new Dictionary<string, int> { ["ammoGasCan"] = 10 });

        bool result = TravelCostCalculator.HasSufficientItems(inventory, "ammoGasCan", 5, out int available);

        Assert.True(result);
        Assert.Equal(10, available);
    }

    [Fact]
    public void HasSufficientItems_NotEnoughItems_ReturnsFalse()
    {
        var inventory = new FakePlayerInventory(new Dictionary<string, int> { ["ammoGasCan"] = 3 });

        bool result = TravelCostCalculator.HasSufficientItems(inventory, "ammoGasCan", 5, out int available);

        Assert.False(result);
        Assert.Equal(3, available);
    }

    [Fact]
    public void ConsumeItems_ExactAmountRemoved_NotUnderOrOverConsumed()
    {
        var inventory = new FakePlayerInventory(new Dictionary<string, int> { ["ammoGasCan"] = 10 });

        InventoryConsumptionResult result = TravelCostCalculator.ConsumeItems(inventory, "ammoGasCan", 5, 10);

        Assert.False(result.UnderConsumed);
        Assert.False(result.OverConsumed);
        Assert.Equal(5, result.Removed);
        Assert.Equal(5, result.RemainingAfter);
    }

    [Fact]
    public void ConsumeItems_RemovedLessThanRequested_UnderConsumedTrue()
    {
        var inventory = new FakePlayerInventory(
            new Dictionary<string, int> { ["ammoGasCan"] = 10 },
            removeItemHandler: (_, _) => 2);

        InventoryConsumptionResult result = TravelCostCalculator.ConsumeItems(inventory, "ammoGasCan", 5, 10);

        Assert.True(result.UnderConsumed);
        Assert.False(result.OverConsumed);
    }

    [Fact]
    public void ConsumeItems_RemovedMoreThanRequested_OverConsumedTrue()
    {
        var inventory = new FakePlayerInventory(
            new Dictionary<string, int> { ["ammoGasCan"] = 10 },
            removeItemHandler: (_, _) => 8);

        InventoryConsumptionResult result = TravelCostCalculator.ConsumeItems(inventory, "ammoGasCan", 5, 10);

        Assert.False(result.UnderConsumed);
        Assert.True(result.OverConsumed);
    }

    [Fact]
    public void FormatItemDisplayName_NullSettings_ReturnsEmpty()
    {
        string result = TravelCostCalculator.FormatItemDisplayName(null, new FakeLocalizationProvider());

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FormatItemDisplayName_LocalizationSucceeds_ReturnsLocalizedText()
    {
        var settings = new TravelCostSettings { ItemName = "ammoGasCan", ItemDisplayName = "Gas Can" };
        var localization = new FakeLocalizationProvider(new Dictionary<string, string> { ["ammoGasCan"] = "Gasoline Can" });

        string result = TravelCostCalculator.FormatItemDisplayName(settings, localization);

        Assert.Equal("Gasoline Can", result);
    }

    [Fact]
    public void FormatItemDisplayName_LocalizationMissing_FallsBackToItemDisplayName()
    {
        var settings = new TravelCostSettings { ItemName = "ammoGasCan", ItemDisplayName = "Gas Can" };
        var localization = new FakeLocalizationProvider();

        string result = TravelCostCalculator.FormatItemDisplayName(settings, localization);

        Assert.Equal("Gas Can", result);
    }

    [Fact]
    public void FormatItemDisplayName_NoDisplayNameOrLocalization_FallsBackToItemName()
    {
        var settings = new TravelCostSettings { ItemName = "ammoGasCan", ItemDisplayName = string.Empty };
        var localization = new FakeLocalizationProvider();

        string result = TravelCostCalculator.FormatItemDisplayName(settings, localization);

        Assert.Equal("ammoGasCan", result);
    }

    [Fact]
    public void FormatItemDisplayName_NoItemNameUsesDisplayName()
    {
        var settings = new TravelCostSettings { ItemName = string.Empty, ItemDisplayName = "Casino Coin" };

        string result = TravelCostCalculator.FormatItemDisplayName(settings, new FakeLocalizationProvider());

        Assert.Equal("Casino Coin", result);
    }
}
