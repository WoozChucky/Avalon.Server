--
-- Table structure for table `CreatureTemplate`
--
CREATE TABLE IF NOT EXISTS `CreatureTemplate` (
    `Id` int(10) unsigned NOT NULL DEFAULT 0,
    `Name` char(100) NOT NULL DEFAULT '0',
    `SubName` char(100) DEFAULT NULL,
    `IconName` char(100) DEFAULT NULL,
    `MinLevel` tinyint(3) unsigned NOT NULL DEFAULT 1,
    `MaxLevel` tinyint(3) unsigned NOT NULL DEFAULT 1,
    `SpeedWalk` float NOT NULL DEFAULT 1 COMMENT 'Result of 2.5/2.5, most common value',
    `SpeedRun` float NOT NULL DEFAULT 1.14286 COMMENT 'Result of 8.0/7.0, most common value',
    `SpeedSwim` float NOT NULL DEFAULT 1,
    `Rank` tinyint(3) unsigned NOT NULL DEFAULT 0,
    `UnitClass` tinyint(3) unsigned NOT NULL DEFAULT 0,
    `Family` tinyint(4) NOT NULL DEFAULT 0,
    `Type` tinyint(3) unsigned NOT NULL DEFAULT 0,
    `Exp` smallint(6) NOT NULL DEFAULT 0,
    `LootId` int(10) unsigned NOT NULL DEFAULT 0,
    `MinGold` int(10) unsigned NOT NULL DEFAULT 0,
    `MaxGold` int(10) unsigned NOT NULL DEFAULT 0,
    `AIName` char(64) NOT NULL DEFAULT '',
    `MovementType` tinyint(3) unsigned NOT NULL DEFAULT 0,
    `DetectionRange` float NOT NULL DEFAULT 20,
    `MovementId` int(10) unsigned NOT NULL DEFAULT 0,
    `ScriptName` char(64) NOT NULL DEFAULT '',
    `HealthModifier` float NOT NULL DEFAULT 1,
    `ManaModifier` float NOT NULL DEFAULT 1,
    `ArmorModifier` float NOT NULL DEFAULT 1,
    `ExperienceModifier` float NOT NULL DEFAULT 1,
    `RegenHealth` tinyint(3) unsigned NOT NULL DEFAULT 1,
    `DmgSchool` tinyint(4) NOT NULL DEFAULT 0,
    `DamageModifier` float NOT NULL DEFAULT 1,
    `BaseAttackTime` int(10) unsigned NOT NULL DEFAULT 0,
    `RangeAttackTime` int(10) unsigned NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    KEY `idx_name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Creature Template System';


LOCK TABLES `CreatureTemplate` WRITE;
INSERT INTO `CreatureTemplate` VALUES
(1,'Uriel',NULL,NULL,1,1,30,1.14286,1,0,0,0,0,0,0,0,0,'',0,20,0,'UrielTownPatrolScript',1,1,1,1,1,0,1,0,0),
(2,'Borin Stoutbeard',NULL,NULL,1,1,1,1.14286,1,0,0,0,0,0,0,0,0,'',0,20,0,'',1,1,1,1,1,0,1,0,0),
(3,'Innkeeper',NULL,NULL,1,1,1,1.14286,1,0,0,0,0,0,0,0,0,'',0,20,0,'',1,1,1,1,1,0,1,0,0);
UNLOCK TABLES;
