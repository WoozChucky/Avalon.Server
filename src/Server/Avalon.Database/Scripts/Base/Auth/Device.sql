--
-- Table structure for table `Device`
--
CREATE TABLE IF NOT EXISTS `Device` (
    `Id` int(10) unsigned NOT NULL AUTO_INCREMENT COMMENT 'Identifier',
    `AccountId` int(10) unsigned NOT NULL COMMENT 'Account Identifier',
    `Name` varchar(120) NOT NULL DEFAULT '' COMMENT 'Device Name',
    `Metadata` varchar(255) NOT NULL DEFAULT '' COMMENT 'Device Metadata',
    `Trusted` tinyint(1) unsigned NOT NULL DEFAULT 0,
    `TrustEnd` datetime NOT NULL DEFAULT '0000-00-00 00:00:00',
    `LastUsage` datetime NOT NULL DEFAULT '0000-00-00 00:00:00',
    PRIMARY KEY (`Id`),
    UNIQUE KEY `idx_name` (`Name`),
    FOREIGN KEY (`AccountId`) REFERENCES `Account` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Device System';
