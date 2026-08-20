using SACCOBlockChainSystem.Models.ViewModels;

public class BalanceSheetModel
{
    public List<BalanceSheetItem> Assets { get; set; } = new();
    public List<BalanceSheetItem> Liabilities { get; set; } = new();
    public List<BalanceSheetItem> Equity { get; set; } = new();

    // Change from read-only to properties with setters
    private decimal _totalAssets;
    public decimal TotalAssets
    {
        get => _totalAssets;
        set => _totalAssets = value; // Add setter
    }

    private decimal _totalLiabilities;
    public decimal TotalLiabilities
    {
        get => _totalLiabilities;
        set => _totalLiabilities = value; // Add setter
    }

    private decimal _totalEquity;
    public decimal TotalEquity
    {
        get => _totalEquity;
        set => _totalEquity = value; // Add setter
    }

    // Computed property
    public decimal TotalLiabilitiesEquity => TotalLiabilities + TotalEquity;
}