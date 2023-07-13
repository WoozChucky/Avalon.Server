DROP TABLE IF EXISTS QuestReward;

CREATE TABLE IF NOT EXISTS QuestReward
(
    QuestId INT UNSIGNED NOT NULL REFERENCES QuestTemplate(Id),
    RewardId INT UNSIGNED NOT NULL REFERENCES QuestRewardTemplate(Id),
    PRIMARY KEY(QuestId, RewardId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM QuestReward WHERE QuestId >= 0;
