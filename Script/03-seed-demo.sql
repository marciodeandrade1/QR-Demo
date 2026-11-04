-- QR-Demo : carga de demonstração com os códigos BR-1001 a BR-1005
-- Uso: mysql --default-character-set=utf8mb4 -u root < Script\03-seed-demo.sql

USE `DbCodigos`;

INSERT INTO `codigos` (`codigo`, `descricao`) VALUES
    ('BR-1001', 'Demonstração QR-Demo'),
    ('BR-1002', 'Demonstração QR-Demo'),
    ('BR-1003', 'Demonstração QR-Demo'),
    ('BR-1004', 'Demonstração QR-Demo'),
    ('BR-1005', 'Demonstração QR-Demo')
AS `novo`
ON DUPLICATE KEY UPDATE `id` = `id`;  -- no-overwrite: mantém o registro existente
