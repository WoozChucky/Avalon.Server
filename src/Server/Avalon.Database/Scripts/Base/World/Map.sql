DROP TABLE IF EXISTS `Map`;
CREATE TABLE IF NOT EXISTS `Map` (
    `id` int NOT NULL DEFAULT '0',
    `map_name` varchar(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
    `map_description` text COLLATE utf8mb4_unicode_ci,
    `atlas` varchar(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
    `directory` varchar(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
    `instance_type` int NOT NULL DEFAULT '0',
    `pvp` bool NOT NULL DEFAULT FALSE,
    `min_level` int NOT NULL DEFAULT '0',
    `max_level` int NOT NULL DEFAULT '0',
    `area_table_id` int NOT NULL DEFAULT '0',
    `loading_screen_id` int NOT NULL DEFAULT '0',
    `corpse_x` float NOT NULL DEFAULT '0',
    `corpse_y` float NOT NULL DEFAULT '0',
    `max_players` int NOT NULL DEFAULT '0',
    PRIMARY KEY (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM `Map`;
