DROP TABLE IF EXISTS `AccountAccess`;
CREATE TABLE IF NOT EXISTS `AccountAccess` (
    `id` int unsigned NOT NULL,
    `level` tinyint unsigned NOT NULL,
    `comment` varchar(255) COLLATE utf8mb4_unicode_ci DEFAULT '',
    PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM `AccountAccess`;
