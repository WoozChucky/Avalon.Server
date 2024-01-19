CREATE TABLE IF NOT EXISTS `__MigrationRecord` (
  `Id` CHAR(36) NOT NULL,
  `Name` VARCHAR(255) NOT NULL,
  `Hash` VARBINARY(255) NOT NULL, -- Assuming a BINARY(16) for representing a byte array in MariaDB
  `ExecutedBy` VARCHAR(60) NOT NULL,
  `ExecutedOn` DATETIME NOT NULL,
  PRIMARY KEY (`Id`)
);
