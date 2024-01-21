DROP TABLE IF EXISTS CharacterActiveQuest;

CREATE TABLE IF NOT EXISTS CharacterActiveQuest
(
    
    CharacterId INT UNSIGNED REFERENCES `Character`(Id),
    QuestId INT UNSIGNED NOT NULL,
    StartedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (CharacterId, QuestId)

) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM CharacterActiveQuest WHERE CharacterId >= 0;
