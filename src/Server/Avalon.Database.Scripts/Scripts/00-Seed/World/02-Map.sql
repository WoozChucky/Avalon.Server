CREATE TABLE IF NOT EXISTS `Map` (
    `Id` int(11) NOT NULL DEFAULT 0,
    `Name` varchar(100) DEFAULT NULL,
    `Description` text DEFAULT NULL,
    `Atlas` varchar(100) DEFAULT NULL,
    `Directory` varchar(100) DEFAULT NULL,
    `InstanceType` int(11) NOT NULL DEFAULT 0,
    `PvP` tinyint(1) NOT NULL DEFAULT 0,
    `MinLevel` int(11) NOT NULL DEFAULT 0,
    `MaxLevel` int(11) NOT NULL DEFAULT 0,
    `AreaTableId` int(11) NOT NULL DEFAULT 0,
    `LoadingScreenId` int(11) NOT NULL DEFAULT 0,
    `CorpseX` float NOT NULL DEFAULT 0,
    `CorpseY` float NOT NULL DEFAULT 0,
    `MaxPlayers` int(11) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

LOCK TABLES `Map` WRITE;
INSERT INTO `Map` VALUES
  (1,'Tutorial.tmx','Glimmerdell','Serene_Village_32x32','Maps/',0,0,1,60,0,0,0,0,32),
  (2,'Village.tmx','Ebonheart Woods','Serene_Village_32x32','Maps/',1,0,1,20,0,0,0,0,5);
UNLOCK TABLES;
