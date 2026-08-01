# iOS App Review Notes

Use the following account from the standard sign-in screen:

- Username: `ios_app_review`
- Password: provided separately in App Store Connect under App Review Information

Accounts are provisioned by the employer or an authorised organisation administrator. Employees cannot register or create an account in this iOS app. The organisation manages account activation, deactivation, password reset, correction, and deletion subject to employment, payroll, tax, and other legal retention requirements. The app handles information supplied through those organisational systems, by employees when they submit, upload, or scan content, through feature-specific device permissions, and through automatic security and diagnostic logs. Employees can contact their organisation administrator or `inquiries@hotbargain.com.au` for a data access, correction, or deletion request.

The privacy policy is available before sign-in from the **Privacy** link and after sign-in from **Settings > Privacy**. The same policy is publicly available at `https://hotbargain.vip/privacy/mobile` without authentication. It explains that authorised service providers must apply equivalent safeguards, that overseas processing is limited to Singapore, and that privacy complaints are acknowledged, investigated, and normally answered in writing within 30 days; unresolved complaints may be escalated to the Office of the Australian Information Commissioner (OAIC).

This iOS production build includes a fully featured offline demo mode for App Review. The demo contains synthetic sample data only and does not connect to the production API, database, object storage, logging service, or device heartbeat service.

All 19 application tabs are available. Data-changing actions update an in-memory demo dataset so reviewers can create, edit, submit, approve, clock in, and view the resulting changes across screens. Demo changes reset when the app is restarted or the reviewer signs out.

No external hardware is required:

- On Product Query, tap **Use Sample Barcode** to load barcode `9330000000017`.
- Label and receipt printing show the existing preview flow and report a simulated success.
- Attendance uses a fixed demonstration location in Brisbane.
- Image uploads retain a local preview and do not upload the selected file.
- Report and data exports create local sample files.

The persistent banner **App Review Demo / Local sample data / Resets on restart or sign-out** identifies the demo session throughout the app.
