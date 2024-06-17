ALTER TABLE `Account` 
    ADD COLUMN `AccessLevel` tinyint(3) unsigned NOT NULL DEFAULT 0 COMMENT 'Access Level', -- 0 = Player, 1 = GM, 2 = Admin, 4 = Tournament, 8 = PTR
    DROP COLUMN `Role`;
