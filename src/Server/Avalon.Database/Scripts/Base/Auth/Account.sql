DROP TABLE IF EXISTS `Account`;
CREATE TABLE IF NOT EXISTS `Account` (
    `id` int unsigned NOT NULL AUTO_INCREMENT COMMENT 'Identifier',
    `username` varchar(32) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
    `salt` varbinary(64) NOT NULL,
    `verifier` varbinary(128) NOT NULL,
    `session_key` binary(40) DEFAULT NULL,
    `totp_secret` varbinary(128) DEFAULT NULL,
    `email` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
    `join_date` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `last_ip` varchar(15) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '127.0.0.1',
    `last_attempt_ip` varchar(15) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '127.0.0.1',
    `failed_logins` int unsigned NOT NULL DEFAULT '0',
    `locked` tinyint unsigned NOT NULL DEFAULT '0',
    `last_login` timestamp NULL DEFAULT NULL,
    `online` int unsigned NOT NULL DEFAULT '0',
    `mute_time` bigint NOT NULL DEFAULT '0',
    `mute_reason` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
    `mute_by` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
    `locale` tinyint unsigned NOT NULL DEFAULT '0',
    `os` varchar(3) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
    `total_time` int unsigned NOT NULL DEFAULT '0',
    PRIMARY KEY (`id`),
    UNIQUE KEY `idx_username` (`username`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Account System';

DELETE FROM `Account`;
