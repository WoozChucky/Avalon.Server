SET FOREIGN_KEY_CHECKS=0; -- to disable them
DROP TABLE IF EXISTS QuestTemplate;
SET FOREIGN_KEY_CHECKS=1; -- to re-enable them

CREATE TABLE IF NOT EXISTS QuestTemplate
(
    
    Id INT UNSIGNED PRIMARY KEY NOT NULL,
    Environment INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = None, 1 = PvE, 2 = PvP, 3 = Dungeon',
    Type INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = None, 1 = Kill, 2 = Collect, 3 = Exploration',
    Rarity INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = None, 1 = Main, 2 = Side, 3 = Legendary',
    
    Title TEXT COLLATE utf8mb4_unicode_ci NOT NULL,
    `Description` TEXT COLLATE utf8mb4_unicode_ci NOT NULL,
    
    GiverCreatureId INT UNSIGNED NOT NULL REFERENCES CreatureTemplate(Id),
    EnderCreatureId INT UNSIGNED NOT NULL REFERENCES CreatureTemplate(Id),
    
    -- Objectives
    CompletionCriteriaId INT UNSIGNED NOT NULL REFERENCES QuestCompletionCriteria(Id),
    
    -- Repeatable Quests
    IsRepeatable BOOLEAN NOT NULL DEFAULT FALSE,
    RepeatFrequency INT UNSIGNED COMMENT '0 = None, 1 = Daily, 2 = Weekly, 3 = Monthly',
    
    -- Acceptance Criteria with columns inline instead of a separate table for now
    LevelRequirement INT UNSIGNED NOT NULL DEFAULT 0,
    RequiredQuestId INT UNSIGNED REFERENCES QuestTemplate(Id),
    ClassRequirement INT UNSIGNED
    

) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM QuestTemplate WHERE Id >= 0;
