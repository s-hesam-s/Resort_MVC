# Vendor dependency boundary

The unused jquery-ajax-unobtrusive 3.2.6 component and its LibMan restore entry were removed. Its only layout references were commented out; application views do not load that component. Removing its historical package.json also removes the unused broad `jquery >=1.8` dependency declaration and Gulp build dependencies from the distributed web root. Active local jQuery, validation, Bootstrap and DataTables resources remain unchanged.

`python scripts/check_vendor_assets.py` rejects restoring the removed package or adding its view import and verifies that active jQuery remains present. This is not a full audit of every vendor dependency. Dependabot alert resolution is verified after GitHub processes the merged manifest removal; no alert is manually dismissed. No application host, database, migration or payment operation is needed for these checks.
