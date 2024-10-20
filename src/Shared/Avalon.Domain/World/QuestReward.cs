namespace Avalon.Domain.World;

public class QuestReward
{
    public QuestTemplate Quest { get; set; }
    public uint QuestId { get; set; }

    public QuestRewardTemplate Reward { get; set; }
    public uint RewardId { get; set; }
}
