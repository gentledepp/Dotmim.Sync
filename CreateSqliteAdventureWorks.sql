-- SQLite AdventureWorks Database Schema
-- Converted from CreateAdventureWorksWithSchemas.sql for .NET Framework 4.8 compatibility
-- No seed data - tables and structure only

----------------------------
-- Address table
CREATE TABLE Address(
    AddressID INTEGER PRIMARY KEY AUTOINCREMENT,
    AddressLine1 TEXT NOT NULL,
    AddressLine2 TEXT NULL,
    City TEXT NULL,
    StateProvince TEXT NULL,
    CountryRegion TEXT NULL,
    PostalCode TEXT NULL,
    rowguid TEXT NULL,
    ModifiedDate TEXT NULL
);

----------------------------
-- Customer table
CREATE TABLE Customer(
    CustomerID TEXT PRIMARY KEY NOT NULL,
    EmployeeID INTEGER NULL,
    NameStyle INTEGER NOT NULL,
    Title TEXT NULL,
    FirstName TEXT NOT NULL,
    MiddleName TEXT NULL,
    LastName TEXT NOT NULL,
    Suffix TEXT NULL,
    CompanyName TEXT NULL,
    SalesPerson TEXT NULL,
    EmailAddress TEXT NULL,
    Phone TEXT NULL,
    PasswordHash TEXT NULL,
    PasswordSalt TEXT NULL,
    rowguid TEXT NULL,
    ModifiedDate TEXT NULL,
    "Attribute With Space" TEXT NULL
);

----------------------------
-- CustomerAddress table
CREATE TABLE CustomerAddress(
    CustomerID TEXT NOT NULL,
    AddressID INTEGER NOT NULL,
    AddressType TEXT NOT NULL,
    rowguid TEXT NULL,
    ModifiedDate TEXT NULL,
    PRIMARY KEY (CustomerID, AddressID)
);

----------------------------
-- Employee table
CREATE TABLE Employee(
    EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT,
    FirstName TEXT NOT NULL,
    LastName TEXT NOT NULL,
    rowguid TEXT NULL,
    ModifiedDate TEXT NULL
);

----------------------------
-- EmployeeAddress table
CREATE TABLE EmployeeAddress(
    EmployeeID INTEGER NOT NULL,
    AddressID INTEGER NOT NULL,
    AddressType TEXT NOT NULL,
    rowguid TEXT NULL,
    ModifiedDate TEXT NULL,
    PRIMARY KEY (EmployeeID, AddressID)
);

----------------------------
-- Log table
CREATE TABLE Log(
    Oid TEXT PRIMARY KEY NOT NULL,
    TimeStampDate TEXT NULL,
    Operation TEXT NULL,
    ErrorDescription TEXT NULL,
    OptimisticLockField INTEGER NULL,
    GCRecord INTEGER NULL
);

----------------------------
-- Posts table
CREATE TABLE Posts(
    PostId INTEGER PRIMARY KEY AUTOINCREMENT,
    Title TEXT NULL
);

----------------------------
-- PostTag table
CREATE TABLE PostTag(
    PostId INTEGER NOT NULL,
    TagId INTEGER NOT NULL,
    PRIMARY KEY (PostId, TagId)
);

----------------------------
-- PricesList table
CREATE TABLE PricesList(
    PriceListId TEXT PRIMARY KEY NOT NULL,
    Description TEXT NOT NULL,
    "From" TEXT NULL,
    "To" TEXT NULL
);

----------------------------
-- PricesListCategory table
CREATE TABLE PricesListCategory(
    PriceListId TEXT NOT NULL,
    PriceCategoryId TEXT NOT NULL,
    PRIMARY KEY (PriceListId, PriceCategoryId)
);

----------------------------
-- PricesListDetail table
CREATE TABLE PricesListDetail(
    PriceListId TEXT NOT NULL,
    PriceCategoryId TEXT NOT NULL,
    PriceListDettailId TEXT NOT NULL,
    ProductId TEXT NOT NULL,
    ProductDescription TEXT NOT NULL,
    Amount REAL NOT NULL,
    Discount REAL NOT NULL,
    Total REAL NOT NULL,
    MinQuantity INTEGER NULL,
    PRIMARY KEY (PriceListId, PriceCategoryId, PriceListDettailId)
);

----------------------------
-- Tags table
CREATE TABLE Tags(
    TagId INTEGER PRIMARY KEY AUTOINCREMENT,
    Text TEXT NULL
);

----------------------------
-- Product table
CREATE TABLE Product(
    ProductID TEXT PRIMARY KEY NOT NULL,
    Name TEXT NOT NULL,
    ProductNumber TEXT NULL,
    Color TEXT NULL,
    StandardCost REAL NULL,
    ListPrice REAL NULL,
    Size TEXT NULL,
    Weight REAL NULL,
    ProductCategoryID TEXT NULL,
    ProductModelID INTEGER NULL,
    SellStartDate TEXT NULL,
    SellEndDate TEXT NULL,
    DiscontinuedDate TEXT NULL,
    ThumbNailPhoto BLOB NULL,
    ThumbnailPhotoFileName TEXT NULL,
    rowguid TEXT NULL,
    ModifiedDate TEXT NULL
);

----------------------------
-- ProductCategory table
CREATE TABLE ProductCategory(
    ProductCategoryID TEXT PRIMARY KEY NOT NULL,
    ParentProductCategoryId TEXT NULL,
    Name TEXT NOT NULL,
    rowguid TEXT NULL,
    ModifiedDate TEXT NULL,
    "Attribute With Space" TEXT NULL
);

----------------------------
-- ProductModel table
CREATE TABLE ProductModel(
    ProductModelID INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    CatalogDescription TEXT NULL,
    rowguid TEXT NULL,
    ModifiedDate TEXT NULL
);

----------------------------
-- SalesOrderDetail table
CREATE TABLE SalesOrderDetail(
    SalesOrderDetailID INTEGER PRIMARY KEY AUTOINCREMENT,
    SalesOrderID INTEGER NOT NULL,
    OrderQty INTEGER NOT NULL,
    ProductID TEXT NOT NULL,
    UnitPrice REAL NOT NULL,
    UnitPriceDiscount REAL NOT NULL,
    LineTotal REAL NULL,
    rowguid TEXT NULL,
    ModifiedDate TEXT NULL
);

----------------------------
-- SalesOrderHeader table
CREATE TABLE SalesOrderHeader(
    SalesOrderID INTEGER PRIMARY KEY AUTOINCREMENT,
    RevisionNumber INTEGER NOT NULL,
    OrderDate TEXT NULL,
    DueDate TEXT NULL,
    ShipDate TEXT NULL,
    Status INTEGER NOT NULL,
    OnlineOrderFlag INTEGER NOT NULL,
    SalesOrderNumber TEXT NOT NULL,
    PurchaseOrderNumber TEXT NULL,
    AccountNumber TEXT NULL,
    CustomerID TEXT NOT NULL,
    ShipToAddressID INTEGER NULL,
    BillToAddressID INTEGER NULL,
    ShipMethod TEXT NOT NULL,
    CreditCardApprovalCode TEXT NULL,
    SubTotal REAL NOT NULL,
    TaxAmt REAL NOT NULL,
    Freight REAL NOT NULL,
    TotalDue REAL NOT NULL,
    Comment TEXT NULL,
    rowguid TEXT NULL,
    ModifiedDate TEXT NULL
);

----------------------------
-- Create indexes
CREATE INDEX IX_Address_StateProvince ON Address(StateProvince);
CREATE INDEX IX_Address_City_StateProvince ON Address(City, StateProvince, PostalCode, CountryRegion);
CREATE INDEX IX_Customer_EmailAddress ON Customer(EmailAddress);

-- Foreign key constraints
-- Note: SQLite requires PRAGMA foreign_keys = ON at runtime for these to be enforced

----------------------------
-- Customer foreign keys
CREATE INDEX IX_Customer_EmployeeID ON Customer(EmployeeID);

----------------------------
-- CustomerAddress foreign keys
CREATE INDEX IX_CustomerAddress_AddressID ON CustomerAddress(AddressID);

----------------------------
CREATE INDEX IX_CustomerAddress_CustomerID ON CustomerAddress(CustomerID);

----------------------------
-- EmployeeAddress foreign keys
CREATE INDEX IX_EmployeeAddress_AddressID ON EmployeeAddress(AddressID);

----------------------------
CREATE INDEX IX_EmployeeAddress_EmployeeID ON EmployeeAddress(EmployeeID);

----------------------------
-- PostTag foreign keys
CREATE INDEX IX_PostTag_PostId ON PostTag(PostId);

----------------------------
CREATE INDEX IX_PostTag_TagId ON PostTag(TagId);

----------------------------
-- PricesListCategory foreign keys
CREATE INDEX IX_PricesListCategory_PriceListId ON PricesListCategory(PriceListId);

----------------------------
-- PricesListDetail foreign keys
CREATE INDEX IX_PricesListDetail_PriceListId_PriceCategoryId ON PricesListDetail(PriceListId, PriceCategoryId);

----------------------------
-- Product foreign keys
CREATE INDEX IX_Product_ProductCategoryID ON Product(ProductCategoryID);
----------------------------
CREATE INDEX IX_Product_ProductModelID ON Product(ProductModelID);

----------------------------
-- ProductCategory foreign keys
CREATE INDEX IX_ProductCategory_ParentProductCategoryId ON ProductCategory(ParentProductCategoryId);

----------------------------
-- SalesOrderDetail foreign keys
CREATE INDEX IX_SalesOrderDetail_ProductID ON SalesOrderDetail(ProductID);
CREATE INDEX IX_SalesOrderDetail_SalesOrderID ON SalesOrderDetail(SalesOrderID);

----------------------------
-- SalesOrderHeader foreign keys
CREATE INDEX IX_SalesOrderHeader_BillToAddressID ON SalesOrderHeader(BillToAddressID);
CREATE INDEX IX_SalesOrderHeader_ShipToAddressID ON SalesOrderHeader(ShipToAddressID);
CREATE INDEX IX_SalesOrderHeader_CustomerID ON SalesOrderHeader(CustomerID);
