# נוהל מדידה שבועי ו-ROI

מבוסס על סעיף 6 (מדדים) וסעיף 4 (שערים) ב-`docs/ROLLOUT-PLAN.he.md`.
**כלל ברזל:** מספר שאף אחד לא מדד נשאר ריק. לא ממלאים הערכות, לא ממציאים ערכי "לפני" ועלות.

> תלות: הנוהל וה-script נשענים על `run-report.json` ועל `REVITCLAUDE_ROOT` (PR #10) ועל `run_report_dashboard.py` (PR #12). ה-PR הזה מבוסס על הענף של #12.

## 1. קובץ שבועי אחד

`REVITCLAUDE_ROOT\metrics\YYYY-WW.xlsx` (שבוע ISO, למשל `2026-46.xlsx`). נבנה מהפקודה:

```
python mcp-server\scripts\weekly_metrics.py %REVITCLAUDE_ROOT% --week 2026-46 --manual manual.csv --before before.csv
```

גיליונות: `Summary` (6 המדדים), `PerFloor`, `ROI`, `Runs`. הקובץ נשמר בתיקייה המשותפת ולא נערך ידנית, רק נבנה מחדש.
קבצי הקלט הידניים נשמרים לצידו: `metrics\YYYY-WW.manual.csv`, ו-`metrics\before.csv` (אחד לכל הזמן).

## 2. מאיפה כל מספר מגיע

| # | מדד | מקור | מי רושם | יעד |
| --- | --- | --- | --- | --- |
| 1 | כיסוי: נקודות עם כלל / נקודות בדוגמה | אין בשדות run-report. נספר מקובץ ההצעה (proposal) או מפלט `learn_design_pattern`. **כרגע אין עמודה ב-CSV** (ראו שאלות) | המתכנן/ת | 90% ומעלה |
| 2 | איכות מיקום | `run-report.json`: `placed`, `failed`, `remaining`, `proposalTotal`. הזזות ונשמטים (`moved`, `dropped`): מפלט `review_model`, נרשמים ב-`manual.csv` | אוטומטי + המתכנן/ת | נשמט 5% לכל היותר |
| 3 | עמידה בכללים | `review_model` לפני ואחרי ההצבה: `review_errors_before`, `review_errors_after` | המתכנן/ת | 0 שגיאות |
| 4 | הסכמה עם המהנדס | הפרש ספירות Claude מול ספירה ידנית של המהנדס, באחוזים (`count_diff_pct`). הפירוט לפי תפקיד וסוג חדר נשאר בהערות המהנדס | המהנדס/ת המאשר/ת | ±10% |
| 5 | זמן | `run-report.json`: `seconds` (זמן ריצת ההצבה, מצטבר לקומה). זמן ביקורת מהנדס: סטופר, `review_minutes`. זמן snapshot וחלק offline: **לא נשמר היום** (ראו שאלות) | ריצה: אוטומטי. ביקורת: המהנדס/ת | 15 דקות ביקורת לקומה |
| 6 | עלות | Team dashboard (טוקנים) או API usage, בש"ח, `cost_ils` | מי שיש לו גישה ל-dashboard (ראו שאלות) | ידוע לפני שער שלב 1 |

הערה: השדה `coverage` ב-`run-report.json` הוא חלק ההצעה שהוצב, לא מדד 1 של סעיף 6. ב-Summary הוא מופיע כ"placed % of proposal".

### קבצי הקלט הידניים

`manual.csv` (שורה לכל קומה/הצעה בשבוע; רק `project` ו-`proposal` חובה, תא ריק = לא נמדד):

```
project,proposal,review_minutes,manual_corrections,review_errors_before,review_errors_after,moved,dropped,count_diff_pct,cost_ils
```

`before.csv` (בסיס ידני: כמה לקח וכמה עלה לתכנן קומה כזו בלי Claude; ממלאים מהנדסים, לא מנחשים):

```
project,proposal,minutes,cost_ils
```

המפתח (`project`, `proposal`) זהה לשמות ב-`run-report.json`: `project` = שם התיקייה תחת `Projects`, `proposal` = שם קובץ ההצעה.

## 3. סדר שבועי

1. **כל ריצה:** אוטומטי, `run-report.json` נכתב ליד ההצעה.
2. **אחרי כל קומה:** המתכנן/ת מריץ/ה `review_model`, רושם/ת errors/moved/dropped, ושעון עצר לזמן ביקורת, ב-`manual.csv`.
3. **יום ראשון:** חלוץ הצוות (סעיף 5 בתוכנית) אוסף את ה-CSV, מוסיף עלות מה-dashboard, מריץ את הפקודה. מועד מוצע, ניתן לשינוי.
4. הקובץ נשמר ב-`metrics\`; סאהר קורא את `Summary` מול שערי סעיף 4.

## 4. ROI: מספר אחד

גיליון `ROI`: לכל קומה, **זמן וגם עלות, לפני מול אחרי**.

- זמן אחרי = `seconds/60` (ריצת Claude) + `review_minutes` (ביקורת מהנדס). מחושב רק כשיש `review_minutes`; אחרת ריק.
- זמן לפני = `minutes` מ-`before.csv`. אין ערך, אין חישוב.
- חיסכון = לפני פחות אחרי, ובאחוזים. שער שלב 2 דורש 40% לפחות בקומה טיפוסית.
- עלות: `before.csv` מול `cost_ils` ב-`manual.csv`; ההגדרה (האם כוללת שכר מהנדס) נקבעת בשאלות להלן.
- `manual_corrections` נספר ואינו נכנס לזמן, כי הוא ספירה ולא דקות.

## 5. אימות

`python mcp-server\scripts\weekly_metrics.py --selfcheck` מריץ את הבנייה על נתונים סינתטיים (לא נתוני אמת) ובודק צירוף ריצות, סינון שבוע, חישוב חיסכון, ושללא נתונים ידניים ה-ROI נשאר ריק. לא נבדק מול `run-report.json` אמיתי או מול Excel/Power BI.

## שאלות לאנשים

| מי | שאלה | למה זה חוסם |
| --- | --- | --- |
| עביר / מהנדסי חשמל | כמה דקות ועלות לוקח היום לתכנן ידנית קומה טיפוסית (לפי פרויקט)? | בלי `before.csv` אין ROI ואי אפשר לאמת את שער 40% |
| טופז IT / מי שמנהל את Claude Team | מי יש לו גישה ל-Team dashboard, ואפשר לקבל ייצוא שבועי של טוקנים לפי משתמש? | מדד 6 (עלות) ריק בלעדיו |
| הנהלה / כספים | מה נחשב "עלות לקומה": רק Claude, או גם שעות מהנדס, ובאיזה תעריף? | קובע את נוסחת העלות ב-ROI |
| עביר | האם הצעה (proposal) אחת = קומה אחת? | המפתח של ה-CSV ושל "לקומה" תלוי בכך |
| סאהר | האם להוסיף ל-`run-report.json` שדות: זמן snapshot, זמן חלק offline, ונקודות עם כלל/בדוגמה? | מדדים 1 ו-5 חלקיים בלי זה |
| חלוץ צוות | מי אוסף את ה-CSV ביום ראשון בכל צוות? | אין בעלים, אין קובץ שבועי |
