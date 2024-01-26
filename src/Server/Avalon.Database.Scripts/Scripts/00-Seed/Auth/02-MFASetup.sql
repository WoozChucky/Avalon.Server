CREATE TABLE IF NOT EXISTS `MFASetup` (
    `Id` UUID NOT NULL DEFAULT UUID() COMMENT 'Identifier',
    `AccountId` INT(10) UNSIGNED NOT NULL COMMENT 'Account Identifier',
    `Secret` VARBINARY(128) NOT NULL DEFAULT '' COMMENT 'Device Secret',
    `RecoveryCode1` VARBINARY(128) NOT NULL DEFAULT '',
    `RecoveryCode2` VARBINARY(128) NOT NULL DEFAULT '',
    `RecoveryCode3` VARBINARY(128) NOT NULL DEFAULT '',
    `Status` INT UNSIGNED NOT NULL DEFAULT 1 COMMENT '0 = Confirmed, 1 = Setup, 2 = Reset',
    `CreatedAt` datetime NOT NULL DEFAULT curdate(),
    `ConfirmedAt` datetime NOT NULL DEFAULT '0000-00-00 00:00:00',
    PRIMARY KEY (`Id`),
    FOREIGN KEY (`AccountId`) REFERENCES `Account` (`Id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='MFA System';
