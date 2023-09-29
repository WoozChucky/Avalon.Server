DROP TABLE IF EXISTS CreatureTemplate;
CREATE TABLE IF NOT EXISTS CreatureTemplate (
    `id` int unsigned NOT NULL DEFAULT '0',
    `name` char(100) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '0',
    `subname` char(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
    `IconName` char(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
    `minlevel` tinyint unsigned NOT NULL DEFAULT '1',
    `maxlevel` tinyint unsigned NOT NULL DEFAULT '1',
    
    `speed_walk` float NOT NULL DEFAULT '1' COMMENT 'Result of 2.5/2.5, most common value',
    `speed_run` float NOT NULL DEFAULT '1.14286' COMMENT 'Result of 8.0/7.0, most common value',
    `speed_swim` float NOT NULL DEFAULT '1',
    
    `rank` tinyint unsigned NOT NULL DEFAULT '0',
    
    `unit_class` tinyint unsigned NOT NULL DEFAULT '0',
    `family` tinyint NOT NULL DEFAULT '0',
    `type` tinyint unsigned NOT NULL DEFAULT '0',

    `exp` smallint NOT NULL DEFAULT '0',
    `lootid` int unsigned NOT NULL DEFAULT '0',
    `mingold` int unsigned NOT NULL DEFAULT '0',
    `maxgold` int unsigned NOT NULL DEFAULT '0',
    
    `AIName` char(64) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
    `MovementType` tinyint unsigned NOT NULL DEFAULT '0',
    `detection_range` float NOT NULL DEFAULT '20',
    `movementId` int unsigned NOT NULL DEFAULT '0',
    `ScriptName` char(64) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
    
    `HealthModifier` float NOT NULL DEFAULT '1',
    `ManaModifier` float NOT NULL DEFAULT '1',
    `ArmorModifier` float NOT NULL DEFAULT '1',
    `ExperienceModifier` float NOT NULL DEFAULT '1',
    `RegenHealth` tinyint unsigned NOT NULL DEFAULT '1',
    `dmgschool` tinyint NOT NULL DEFAULT '0',
    `DamageModifier` float NOT NULL DEFAULT '1',
    `BaseAttackTime` int unsigned NOT NULL DEFAULT '0',
    `RangeAttackTime` int unsigned NOT NULL DEFAULT '0',
    
    PRIMARY KEY (`id`),
    KEY `idx_name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Creature Template System';
DELETE FROM CreatureTemplate WHERE id > 0;
