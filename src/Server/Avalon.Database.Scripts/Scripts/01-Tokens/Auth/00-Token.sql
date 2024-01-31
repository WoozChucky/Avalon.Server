--
-- Table structure for table `Token`
--
CREATE TABLE IF NOT EXISTS `Token` (
    `Id` UUID NOT NULL DEFAULT UUID() COMMENT 'Identifier',
    `AccountId` INT(10) UNSIGNED NOT NULL COMMENT 'Account Identifier',
    `Hash` VARBINARY(128) NOT NULL DEFAULT '' COMMENT 'Secret Token Value',
    `Revoked` tinyint(1) unsigned NOT NULL DEFAULT 0,
    `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
    `ExpiresAt` datetime NOT NULL,
    PRIMARY KEY (`Id`),
    FOREIGN KEY (`AccountId`) REFERENCES `Account` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Token System';
