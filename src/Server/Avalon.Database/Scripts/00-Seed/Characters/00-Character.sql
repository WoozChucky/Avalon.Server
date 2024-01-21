DROP TABLE IF EXISTS `Character`;
CREATE TABLE IF NOT EXISTS `Character` (
    `Id` int unsigned NOT NULL AUTO_INCREMENT COMMENT 'Global Unique Identifier',
    `Account` int unsigned NOT NULL DEFAULT '0' COMMENT 'Account Identifier',
    `Name` varchar(12) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    
    `Class` tinyint unsigned NOT NULL DEFAULT '0',
    `Gender` tinyint unsigned NOT NULL DEFAULT '0',
    `Level` tinyint unsigned NOT NULL DEFAULT '0',
    `XP` int unsigned NOT NULL DEFAULT '0',
    `Money` int unsigned NOT NULL DEFAULT '0',
    
    `PositionX` float NOT NULL DEFAULT '0',
    `PositionY` float NOT NULL DEFAULT '0',
    `Map` smallint unsigned NOT NULL DEFAULT '1' COMMENT 'Map Identifier',
    `InstanceId` varchar(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',

    `Online` tinyint unsigned NOT NULL DEFAULT '0',
    `TotalTime` int unsigned NOT NULL DEFAULT '0',
    `LevelTime` int unsigned NOT NULL DEFAULT '0',
    `LogoutTime` int unsigned NOT NULL DEFAULT '0',
    `IsLogoutResting` tinyint(1) unsigned NOT NULL DEFAULT '0',
    `RestBonus` float NOT NULL DEFAULT '0',
    
    `TotalKills` int unsigned NOT NULL DEFAULT '0',
    `TodayKills` smallint unsigned NOT NULL DEFAULT '0',
    `YesterdayKills` smallint unsigned NOT NULL DEFAULT '0',
    `ChosenTitle` int unsigned NOT NULL DEFAULT '0',
    
    `Health` int unsigned NOT NULL DEFAULT '0',
    `Power1` int unsigned NOT NULL DEFAULT '0',
    `Power2` int unsigned NOT NULL DEFAULT '0',
    `Latency` int unsigned DEFAULT '0',
    
    `ActionBars` tinyint unsigned NOT NULL DEFAULT '0',
    `Order` tinyint DEFAULT NULL,
    `CreationDate` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `DeleteDate` int unsigned DEFAULT NULL,
    
    PRIMARY KEY (`Id`),
    KEY `idx_account` (`Account`),
    KEY `idx_online` (`Online`),
    KEY `idx_name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Player System';

DELETE FROM `Character` where Id > 0;
