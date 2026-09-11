SELECT *
FROM   Customers
WHERE  Age > 40
       AND LTRIM(Name) = N'admin';

