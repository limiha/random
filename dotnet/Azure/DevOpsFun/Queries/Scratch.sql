﻿/* SELECT * FROM dbo.BuildCloneTime WHERE DefinitionId = 15 and BuildStartTime > '8/14	/2019' and maxduration > '00:10:00' */

/* SELECT * FROM dbo.JobCloneTime WHERE BuildId = 311446 */
SELECT TOP(10) * FROM dbo.JobCloneTime 
WHERE Duration > '00:07:00'
ORDER BY BuildStartTime DESC