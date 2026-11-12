# דשבורד מדדים ב-Power BI (משימה 17)

מקור הנתונים: `run_report_dashboard.py` מאחד את כל קובצי `run-report.json` לקובץ `run-report-dashboard.xlsx` (גיליונות `Runs`, `PerProject`, `PerUser`). ה-Power BI קורא את הקובץ הזה, לא את ה-JSON.

> **מצב:** לא נבדק ב-Power BI Desktop (לא מותקן כאן). ה-M וה-DAX נכתבו לפי שמות העמודות שהסקריפט כותב. הבדיקה הראשונה נעשית אצל מי שיש לו Power BI Desktop (שלב 3).

## הקמה (15 דק')
1. הרצה ראשונה (מתזמנים שבועית): `python mcp-server\scripts\run_report_dashboard.py %REVITCLAUDE_ROOT% %REVITCLAUDE_ROOT%\metrics\run-report-dashboard.xlsx`
2. Power BI Desktop ← Get data ← Blank query ← Advanced editor ← הדבקת `RunReport.pq`, תיקון `FilePath`.
3. Modeling ← New measure: הדבקת כל שורה מ-`measures.dax`.
4. בניית 4 עמודים לפי הטבלה למטה. Refresh ידני או Scheduled refresh (דורש gateway לנתיב רשת).

## עמודים
| עמוד | ויזואלים | שאלה שהוא עונה |
| --- | --- | --- |
| סיכום | כרטיסים: `Runs`, `Placed %`, `Failed %`, `Active users`; קו `Runs` לפי `WeekStart` | האם משתמשים, והאם ההצבה עובדת? |
| פרויקטים | טבלה לפי `project`: `Attempted`, `Placed %`, `Minutes` | איפה Claude חזק וחלש? |
| משתמשים (שער 2) | עמודות `Runs` לפי `user` ו-`WeekStart` | מי משתמש, מי נעלם? |
| איכות | `Failed %` מול `Drop target` (5%), `Seconds per element` | עמידה ביעד נשמטים |

## מה אין כאן (בכוונה)
- מדדי `review_model` (שגיאות לפני/אחרי), הזזות, הסכמה עם המהנדס, זמן ביקורת ועלות: הם לא נשמרים ב-`run-report.json`. הם נכנסים דרך `manual.csv` ל-`weekly_metrics.py` (`docs/MEASUREMENT.he.md`) וניתן לטעון את `metrics\YYYY-WW.xlsx` כמקור שני אחרי שיצטברו שבועות.
- כיסוי (מדד 1): אין שדה. לא ממציאים.

## בדיקת קבלה
- [ ] Refresh עובד על קובץ אמיתי אחד לפחות (לא נתוני דמה).
- [ ] סכום `Attempted` בעמוד הסיכום = סכום העמודה בגיליון `Runs`.
- [ ] משתמש בלי `user` ב-JSON הישן מופיע כריק ולא מפיל את הטעינה.
