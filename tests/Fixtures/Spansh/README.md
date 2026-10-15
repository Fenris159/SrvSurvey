# Fleet-carrier route regression

`fleet-carrier-41b4.json` is the public Spansh result captured on 2026-10-14 from
`https://spansh.co.uk/api/results/41B4D994-C830-11F1-B393-A7063E1886B9`.
It reproduces the reported 46-stop Sol → Colonia import. No account or game
credentials are included.

The response uses `fuel_in_tank` for remaining fuel and numeric `0`/`1` for
`must_restock`. Flyiedgiae ZQ-S b8-1 requires 953 t; Clooku PO-N c8-115 requires
937 t. Other rows have zero restock amounts, meaning no recommendation. The
final row has a genuine zero tank balance, which must remain visible.

Both Core parsing and Desktop clipboard/search integration tests replay this
same response without contacting Spansh.
