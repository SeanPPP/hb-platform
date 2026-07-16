# iOS App Review Notes

Use the following account from the standard sign-in screen:

- Username: `ios_app_review`
- Password: provided separately in App Store Connect under App Review Information

This iOS production build includes a fully featured offline demo mode for App Review. The demo contains synthetic sample data only and does not connect to the production API, database, object storage, logging service, or device heartbeat service.

All 19 application tabs are available. Data-changing actions update an in-memory demo dataset so reviewers can create, edit, submit, approve, clock in, and view the resulting changes across screens. Demo changes reset when the app is restarted or the reviewer signs out.

No external hardware is required:

- On Product Query, tap **Use Sample Barcode** to load barcode `9330000000017`.
- Label and receipt printing show the existing preview flow and report a simulated success.
- Attendance uses a fixed demonstration location in Brisbane.
- Image uploads retain a local preview and do not upload the selected file.
- Report and data exports create local sample files.

The persistent banner **App Review Demo / Local sample data / Resets on restart or sign-out** identifies the demo session throughout the app.
