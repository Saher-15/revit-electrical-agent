# רשימת בדיקה להתקנה: עביר ומתכנן/ת שני/ה, על המחשב האישי

**משימה 08 · תאריך:** 2026-11-11 · **היקף:** חשמל טופז בלבד · **מבוסס על:** `mcp-server/install-electrical.ps1`, `README.md`, `mcp-server/CHAT.md`

**מה המסמך הזה לא מכסה:** חבילת release, התקנה שקטה ו-`docs/deployment.md` אינם ב-main (התקנה שקטה יושבת ב-PR #15 כטיוטה, deployment בענף של PR #2). שלב 1 בתוכנית ההטמעה הוא התקנה ידנית, וזה מה שמתואר כאן: הסקריפט בונה מקוד מקור על כל מחשב. זמן יעד: 20 דקות אחרי שהדרישות מתקיימות.

## 0. לפני שמתחילים (על המחשב של המשתמש/ת)

| # | בדיקה | איך מוודאים | אם נכשל |
| --- | --- | --- | --- |
| 0.1 | Revit 2025 או 2026 מותקן | פותחים Revit ובודקים גרסה (עזרה > אודות) | מתקינים/מבקשים מ-IT. הסקריפט מקבל `-RevitYear 2025` או `2026` (ברירת מחדל 2026) |
| 0.2 | Revit **סגור** | מנהל המשימות: אין `Revit.exe` | הסקריפט עוצר ומדפיס "Revit is running" (ה-DLL נעולים כשהוא פתוח) |
| 0.3 | `dotnet` (SDK, לא רק runtime) | `dotnet --list-sdks` מציג לפחות SDK אחד | מתקינים .NET SDK. התוסף בנוי ל-net48, ה-SDK בונה אותו |
| 0.4 | `node` ו-`npm` | `node -v` ו-`npm -v` | מתקינים Node.js LTS |
| 0.5 | `claude` (Claude Code CLI) | `claude --version` | מתקינים Claude Code |
| 0.6 | כניסה ל-Claude עם מושב Team של המשתמש/ת | `claude` ואז התחברות בדפדפן | **חוסם:** ראו שאלה 1 למטה (מושבים). אין מפתח API: הכלי משתמש בכניסה של Claude Code |
| 0.7 | `git` וגישה לריפו `Saher-15/revit-electrical-agent` | `git ls-remote https://github.com/Saher-15/revit-electrical-agent` | **חוסם אם הריפו פרטי:** ראו שאלה 2 |
| 0.8 | PowerShell רגיל, **לא כמנהל** | הפקודה הבאה מורצת בחלון רגיל | הסקריפט כותב ל-`%APPDATA%` של המשתמש ול-`claude mcp add -s user`; כמנהל זה ייכתב למשתמש הלא נכון |

כל ארבע הפקודות `dotnet`, `node`, `npm`, `claude` נבדקות גם בסקריפט עצמו; כשאחת חסרה הוא מדפיס `'<tool>' was not found in PATH` ויוצא. אחרי התקנת כלי חדש יש לפתוח חלון PowerShell חדש.

## 1. הורדת המקור

```powershell
cd $env:USERPROFILE
git clone https://github.com/Saher-15/revit-electrical-agent.git
cd revit-electrical-agent
git log -1 --oneline
```
**אימות:** `git log` מציג את ראש ה-main. התקנה נעשית מ-**main בלבד** (לא מענף פיתוח), כדי שכל משתמש יריץ אותה גרסה. רושמים את מזהה הקומיט בטבלת ההתקנות (סעיף 8).

## 2. הרצת הסקריפט (Revit סגור)

```powershell
cd mcp-server
powershell -ExecutionPolicy Bypass -File .\install-electrical.ps1 -RevitYear 2026
```
הסקריפט מדפיס ארבעה שלבים. לכל שלב: מה הוא עושה, איך מוודאים, ומה נפוץ שנכשל.

| שלב | מה קורה | אימות | כשל נפוץ ופתרון |
| --- | --- | --- | --- |
| **0/3** בניית ה-plugin | `dotnet build plugin\RevitMCPPlugin.csproj -c "Debug R26"` (או R25). מעתיק `CLAUDE-electrical.md` ל-`...\revit_mcp_plugin\CLAUDE.md` ול-`Documents\RevitClaude\CLAUDE.md`. מוחק קבצי `.addin` כפולים מהתקנה ידנית ישנה | קיים `%APPDATA%\Autodesk\Revit\Addins\2026\revit_mcp_plugin\RevitMCPPlugin.dll`; קיים `Documents\RevitClaude\CLAUDE.md` | `Plugin build failed`: חסר SDK או Revit API (NuGet) בלי גישת רשת; לשלוח את הפלט ל-IT/סאהר. `Expected ...dll after the build but it is missing`: הגרסה שבחרתם לא תואמת ל-`-RevitYear` |
| **1/3** בניית ה-command set | `dotnet build commandset\RevitMCPCommandSet.csproj`, מעתיק `command.json` | קיים `...\revit_mcp_plugin\Commands\RevitMCPCommandSet\2026\RevitMCPCommandSet.dll` ו-`command.json` לידו | `Command set build failed`: אותה סיבה; לבדוק שהשלב הקודם עבר |
| **2/3** בניית שרת MCP | `npm install` ו-`npm run build` ב-`server\` | קיים `mcp-server\server\build\index.js` | `npm install failed`: רשת/פרוקסי. `Server build failed`: גרסת Node ישנה |
| **3/3** רישום ב-Claude Code | `claude mcp add -s user mcp-server-for-revit -- node ...\server\build\index.js` ואז `claude mcp list` | ברשימה `mcp-server-for-revit` במצב מחובר (Connected) | לא מחובר: להריץ `node "<נתיב>\server\build\index.js"` ולקרוא את השגיאה; לרוב נתיב שגוי או build חסר |

סיום תקין: "Done. Next, in Revit ...". **הסקריפט לא מעתיק את ה-skill ולא את `office.json`:** זה סעיף 3.

## 3. ה-skill וקובץ המשרד (ידני, עד שייכנס לסקריפט)

```powershell
cd $env:USERPROFILE\revit-electrical-agent
New-Item -ItemType Directory -Force "$env:USERPROFILE\.claude\skills" | Out-Null
Copy-Item -Recurse -Force skill\design-by-example "$env:USERPROFILE\.claude\skills\"
```
**אימות:** קיים `%USERPROFILE%\.claude\skills\design-by-example\SKILL.md`.

**קובץ `office.json`:** אל תעתיקו ברירת מחדל על מחשב של משתמש שנייה בלי החלטה. המקור הנכון הוא הקובץ המאושר של המשרד (ראו `OFFICE-JSON-REVIEW.he.md`). עד שהוא מאושר: מעתיקים את הקובץ מהמחשב של סאהר (`Documents\RevitClaude\office.json`), לא את `office-config\default.json`, כי ההבדלים ביניהם מתועדים שם. אם אין `office.json`, ה-skill נופל חזרה ל-`default.json` שבריפו, כלומר משפחות ריקות.

## 4. הגדרות בתוך Revit

1. פותחים Revit 2026 ופרויקט (עותק **מקומי**, לא מודל ענן; ראו כללי בטיחות).
2. בלשונית Add-Ins יש קבוצה **Revit MCP**. אימות: רואים שלושה כפתורים (Open Server / Settings / Open Claude).
3. Add-Ins > Revit MCP > **Settings**: Select All, מורידים סימון מ-`send_code_to_revit`, Save. **אימות:** בכל רשימת הפקודות מופיעות פקודות החשמל, הניתוח והשרטוט; `send_code_to_revit` לא מסומן.
4. Add-Ins > Revit MCP > **Open Claude**: מפעיל את שרת ה-socket (פורט 8080) ופותח Claude Code בתיקייה `Documents\RevitClaude`. **אימות:** הנקודה בכותרת החלונית ירוקה.

כשל נפוץ: Revit נפתח בלי הכפתורים. בודקים שקובץ `mcp-servers-for-revit.addin` קיים ב-`%APPDATA%\Autodesk\Revit\Addins\2026\` ושהוא יחיד (הסקריפט מוחק כפילויות), ושההתקנה היא לאותה שנת Revit שנפתחה. Revit שואל "לטעון תוסף לא חתום": זה צפוי עד חתימת קוד (שלב 2); לבחור Always Load רק אחרי שסאהר אישר.

## 5. בדיקת עשן (סדר קבוע, כולל מה צריך לראות)

| # | מה מבקשים מ-Claude | תוצאה תקינה |
| --- | --- | --- |
| 5.1 | "say hello to Revit" | תשובה מ-Revit (תוסף וסוקט מחוברים) |
| 5.2 | "get_electrical_summary of the open model" | סיכום מספרים של המודל הפתוח (כלי הסקריפט עצמו מציע בדיוק את זה) |
| 5.3 | "run get_project_setup" | מציג את תיקיית הפרויקט, RULES.md אם קיים, ומסמכים ממתינים |
| 5.4 | "get_available_family_types" | רשימת משפחות המודל. מציגים את הרשימה גם לאישור OFFICE-JSON-REVIEW |
| 5.5 | בקשה לשנות משהו קטן בפרויקט בדיקה (`TEST`) | Claude מציג תוכנית ומחכה לאישור; שום דבר לא נשמר או מסונכרן (Save/Sync אסורים) |

אם 5.1 נכשל: Revit לא התחיל את השרת (כפתור Open Server), או פורט 8080 תפוס. אם 5.1 עובר ו-5.2 נכשל: Claude Code לא רואה את השרת; `claude mcp list` שוב.

## 6. כללי בטיחות שמסבירים בעת ההתקנה
- **אין Save ואין Sync** מצד Claude. המשתמש/ת שומר/ת בעצמו/ה.
- פיילוט על **עותק מקומי** של המודל, לא על מודל ACC פעיל.
- `send_code_to_revit` כבוי.
- אחרי כל build מחדש מפעילים מחדש את לקוח ה-MCP (ענף `docs/restart-after-rebuild`, PR #9).

## 7. עדכון והסרה
- **עדכון:** `git pull` ב-main, Revit סגור, מריצים שוב את הסקריפט. הוא מחליף את הרישום ב-Claude Code (`mcp remove` ואז `add`). מעתיקים שוב את ה-skill.
- **הסרה:** `claude mcp remove -s user mcp-server-for-revit`; מוחקים `%APPDATA%\Autodesk\Revit\Addins\<שנה>\revit_mcp_plugin` ואת `mcp-servers-for-revit.addin`. `Documents\RevitClaude` נשאר (כללים ופרויקטים).

## 8. טבלת התקנות (למלא אחרי כל התקנה)

| משתמש/ת | תאריך | Revit | קומיט | סעיפים שעברו (0-5) | הערות |
| --- | --- | --- | --- | --- | --- |
| עביר | | | | | |
| מתכנן/ת שני/ה | | | | | |

## שאלות לאנשים

| מי | שאלה | למה זה חוסם |
| --- | --- | --- |
| סאהר / IT | האם נרכשו מושבי Claude Team ל-2-3 המשתמשים, ובאיזה אימייל כל אחד? | בלי כניסה ל-Claude אין שימוש בכלי (סעיף 0.6) |
| סאהר | האם הריפו פרטי, ומי נותן לעביר ולמתכנן/ת השני/ה גישת קריאה? | `git clone` נכשל בלי גישה (סעיף 0.7) |
| IT | האם מותר להתקין .NET SDK, Node.js ו-Claude Code בלי הרשאות מנהל על מחשבי המתכננים, ואיזו גרסת Revit בפועל (2025 או 2026) אצל כל אחד? | בלי SDK אין build; הגרסה קובעת את `-RevitYear` |
| IT | האם חוסם אבטחה/EDR את טעינת DLL לא חתום ל-Revit, והאם יש חריג? | הכפתורים לא ייטענו |
| עביר | באיזה פרויקט וקובץ מקומי מריצים את בדיקת העשן, ומי מגבה את המודל לפני? | סעיף 5 דורש עותק מקומי בטוח |
| סאהר | מי המתכנן/ת השני/ה? (שלב 1) | בלי שם אי אפשר לתאם התקנה |
