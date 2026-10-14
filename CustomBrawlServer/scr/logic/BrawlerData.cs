namespace CustomBrawlServer.Logic;

public class BrawlerData
{
    public int CharacterId { get; set; }
    public int CardId { get; set; }
    public bool Unlocked { get; set; } = true;
    public int Level { get; set; } = 11;
    public int Trophies { get; set; } = 0;
    public int HighestTrophies { get; set; } = 0;
    public int SelectedSkin { get; set; } = 0;
    public int PowerPoints { get; set; } = 0;

    /// <summary>Equipped star power card id (0 = none chosen yet).</summary>
    public int SelectedStarPower { get; set; } = 0;

    /// <summary>Equipped gadget card id (0 = none chosen yet).</summary>
    public int SelectedGadget { get; set; } = 0;
}
