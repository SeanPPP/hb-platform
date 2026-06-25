IF COL_LENGTH('StoreLocalSupplierInvoiceDetails', 'AdditionalBarcodesJson') IS NULL
BEGIN
    ALTER TABLE [StoreLocalSupplierInvoiceDetails]
    ADD [AdditionalBarcodesJson] nvarchar(max) NULL;
END;
