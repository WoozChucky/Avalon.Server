DROP TABLE IF EXISTS `Character`;
CREATE TABLE IF NOT EXISTS `Character` (
    `id` int unsigned NOT NULL DEFAULT '0' COMMENT 'Global Unique Identifier',
    `account` int unsigned NOT NULL DEFAULT '0' COMMENT 'Account Identifier',
    `name` varchar(12) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    
    `class` tinyint unsigned NOT NULL DEFAULT '0',
    `gender` tinyint unsigned NOT NULL DEFAULT '0',
    `level` tinyint unsigned NOT NULL DEFAULT '0',
    `xp` int unsigned NOT NULL DEFAULT '0',
    `money` int unsigned NOT NULL DEFAULT '0',
    
    `position_x` float NOT NULL DEFAULT '0',
    `position_y` float NOT NULL DEFAULT '0',
    `map` smallint unsigned NOT NULL DEFAULT '0' COMMENT 'Map Identifier',
    `instance_id` int unsigned NOT NULL DEFAULT '0',

    `online` tinyint unsigned NOT NULL DEFAULT '0',
    `total_time` int unsigned NOT NULL DEFAULT '0',
    `level_time` int unsigned NOT NULL DEFAULT '0',
    `logout_time` int unsigned NOT NULL DEFAULT '0',
    `is_logout_resting` tinyint unsigned NOT NULL DEFAULT '0',
    `rest_bonus` float NOT NULL DEFAULT '0',
    
    `total_kills` int unsigned NOT NULL DEFAULT '0',
    `today_kills` smallint unsigned NOT NULL DEFAULT '0',
    `yesterday_kills` smallint unsigned NOT NULL DEFAULT '0',
    `chosen_title` int unsigned NOT NULL DEFAULT '0',
    
    `health` int unsigned NOT NULL DEFAULT '0',
    `power1` int unsigned NOT NULL DEFAULT '0',
    `power2` int unsigned NOT NULL DEFAULT '0',
    `latency` int unsigned DEFAULT '0',
    
    `action_bars` tinyint unsigned NOT NULL DEFAULT '0',
    `order` tinyint DEFAULT NULL,
    `creation_date` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `delete_date` int unsigned DEFAULT NULL,
    
    PRIMARY KEY (`id`),
    KEY `idx_account` (`account`),
    KEY `idx_online` (`online`),
    KEY `idx_name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Player System';

DELETE FROM `Character`;
