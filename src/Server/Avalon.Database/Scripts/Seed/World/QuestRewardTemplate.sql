DROP TABLE IF EXISTS QuestRewardTemplate;

CREATE TABLE IF NOT EXISTS QuestRewardTemplate
(

    Id INT UNSIGNED PRIMARY KEY NOT NULL,
    `Description` TEXT COLLATE utf8mb4_unicode_ci NOT NULL,
    Type INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = None, 1 = Item, 2 = Gold, 3 = Experience, 4 = Skill, 5 = Title, 6 = Other',
    `Value` INT UNSIGNED NOT NULL,
    `Count` INT UNSIGNED NOT NULL DEFAULT 1

) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM QuestRewardTemplate WHERE Id >= 0;
