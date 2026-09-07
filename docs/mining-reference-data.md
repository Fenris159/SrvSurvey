# Mining reference data

`mining-rings.json.gz` contains factual mining locations converted from EliteMining's installation database, overlap/RES CSVs and system coordinates at commit `7524580059d23753f3583a6c03821983083740e2` (Viper-Dude/EliteMining, GPL-3.0; the repository LICENSE applies). No executable Python or SQLite database is loaded by SrvSurvey.

Source: https://github.com/Viper-Dude/EliteMining/tree/7524580059d23753f3583a6c03821983083740e2/app/data

The installation database contains 43,221 mineral observations, consolidated by system/ring. Additional CSV annotations are retained. Body names are normalized to include their system. Missing coordinates remain unknown. Numeric values incorrectly stored in the reference reserve column are omitted rather than labeled as reserve levels. The bundled data is historical; live scans and saved online results take precedence. A search returns at most 500 nearby rings. The separate workbook contains 3,237 rows and is not the complete installation database.
