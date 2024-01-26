DROP TABLE IF EXISTS QuestCompletionCriteria;

CREATE TABLE IF NOT EXISTS QuestCompletionCriteria
(
    
    Id INT UNSIGNED PRIMARY KEY NOT NULL,
    `Description` TEXT COLLATE utf8mb4_unicode_ci NOT NULL,
    MapId INT REFERENCES Map(Id),
    CreatureId INT UNSIGNED REFERENCES CreatureTemplate(Id),
    ItemId INT UNSIGNED REFERENCES ItemTemplate(Id),
    LocationX FLOAT UNSIGNED,
    LocationY FLOAT UNSIGNED,
    RequirementType VARCHAR(50),
    RequirementValue INT UNSIGNED

) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM QuestCompletionCriteria WHERE Id >= 0;
