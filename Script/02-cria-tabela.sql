-- QR-Demo : tabela de códigos cadastrados / lidos pelo ACR122U
-- Uso: mysql --default-character-set=utf8mb4 -u root < Script\02-cria-tabela.sql
--      (execute o 01-cria-database.sql antes)

USE `DbCodigos`;

CREATE TABLE IF NOT EXISTS `codigos` (
    `id`           INT UNSIGNED NOT NULL AUTO_INCREMENT,
    `codigo`       VARCHAR(50)  NOT NULL COMMENT 'Código exibido/impresso (ex.: BR-1001)',
    `uid`          VARCHAR(32)  NULL     COMMENT 'UID do cartão RFID (ACR122U), hexadecimal',
    `descricao`    VARCHAR(200) NULL     COMMENT 'Descrição livre da associação',
    `data_leitura` DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_codigos_codigo` (`codigo`),
    KEY `ix_codigos_uid` (`uid`)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci;
