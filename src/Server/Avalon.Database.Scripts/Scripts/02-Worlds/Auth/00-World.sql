--
-- Table structure for table `World`
--
CREATE TABLE IF NOT EXISTS `World` (
    `Id` INT(10) UNSIGNED NOT NULL COMMENT 'World Identifier',
    `Name` VARCHAR(32) NOT NULL COMMENT 'World Name',
    `Type` TINYINT(3) UNSIGNED NOT NULL COMMENT 'World Type', -- 0 = PvE, 1 = PvP, 2 = Event
    `AccessLevelRequired` TINYINT(3) UNSIGNED NOT NULL COMMENT 'Access Level bitmask required to access this world', -- 0 = Player, 1 = GM, 2 = Admin, 4 = Tournament, 8 = PTR
    `Host` VARCHAR(255) NOT NULL COMMENT 'World Host Address',
    `Port` INT(5) UNSIGNED NOT NULL COMMENT 'World Port',
    `MinVersion` VARCHAR(32) NOT NULL COMMENT 'Minimum Client Version',
    `Version` VARCHAR(32) NOT NULL COMMENT 'Expected Client Version',
    `Status` TINYINT(3) UNSIGNED NOT NULL COMMENT 'World Status', -- 0 = Offline, 1 = Online, 2 = Maintenance
    `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
    `UpdatedAt` datetime NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Game Worlds';
