# הצעה למילוי בלוק `families` ב-office.json (משימה 14)

**מקור:** ספירת המשפחות והסוגים שמופיעים בפועל בקומה 02 של KYM-EL-101-5 (snapshot 12/11/2026, מתוכנן ידנית על ידי עביר) מול `get_available_family_types` מ-Revit. **לא נכתב** ל-`office.json` החי; `OFFICE-FAMILIES-PROPOSAL.json` הוא הצעה למיזוג אחרי אישור.

| תפקיד | משפחה | סוג | שימושים בקומה 02 | ביטחון |
| --- | --- | --- | --- | --- |
| socketDouble | Tpz_3D_EF_Socket 1-ph Under Plaster | 16A 2X ח"ק חד פאזי | 31 | גבוה |
| socketSingle | Tpz_3D_EF_Socket 1-ph Under Plaster | 16A ח"ק חד פאזי | 88 | גבוה |
| socket3Phase | (ריק) | | 0 | לא נקבע, ראו שאלה 1 |
| switch1 | Tpz_3D_LD_Lighting buttons WP Under Plaster | מפסק יחיד | 51 | גבוה |
| switch2way | Tpz_3D_LD_Lighting buttons WP Under Plaster | מפסק מחליף | 46 | גבוה |
| shutterSwitch | Tpz_3D_EF_3 mode switch Under Plaster_new | מפסק תריס שלושה מצבים תחת הטיח | 27 | גבוה |
| ceilingLight | Tpz_3D_LF_ Lighting Pointes | נ"ק תאורה על התקרה | 84 | בינוני: זו "נקודת תאורה" ולא גוף עם לומנים (ראו שאלה 2) |
| ceilingLightIP44 | Tpz_3D_LF_ Lighting Pointes | נ"ק תאורה מ"ם על התקרה | 29 | בינוני: הנחתי ש"מ"ם" = מוגן מים |
| tvOutlet | Tpz_3D_EF_Socket TV | TV | 19 | גבוה |
| dataOutlet | Tpz_3D_CD_Computer point | נק' מחשב תה"ט | 19 | גבוה |
| acPoint | Tpz_3D_EF_Input point | נק' הזנה חד פאזית למ.א | 9 | גבוה |
| panel | Tpz_3D_EQ_Electrical Panel | Electrical Panel | 6 | גבוה |

## שאלות לעביר
1. שקע/נקודה תלת-פאזית: איזו משפחה (Vallter 3ph? Disconnector 3-Ph-WP?) או שלא בשימוש בדירות?
2. `ceilingLight`: משפחת "נקודת תאורה" אינה גוף תאורה עם לומנים. בלוק זה מכיל `lumens: 1600` בברירת המחדל. האם מחשבים תאורה לפי לומנים בכלל, או רק נקודות?
3. האם "מ"ם" בסוג תאורה הוא "מוגן מים" (IP44)?
4. האם אותן משפחות חלות על כל פרויקטי טופז או רק על KYM?

## מגבלה
נגזר מקומה אחת. משפחות שלא הופיעו בה (למשל גוף חירום, גלאי נוכחות, רמקולים) לא נוספו לתפקידים.
