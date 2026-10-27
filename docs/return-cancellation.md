# Resident return cancellation

`POST /api/returns/{id}:cancel` requires the authenticated Resident who owns the return. Guest pickup codes and sessions never authorize cancellation. GET detail/list now include `canCancel` and optional `cancellation` (`status`, `requestedAt`, `completedAt`, `canRetrieve`, `message`). Repeated cancellation returns the persisted result without issuing another command.

A Created return with no allocation, deposit or unlock history becomes Cancelled immediately. Any Allocated/Deposited return instead records PendingVerification, clears the guest token, hides its old pickup code, rejects new guest sessions and ordinary return unlocks, and retains occupancy/reservations. Cancellation does not infer an empty compartment from a closed door or a command acknowledgement. An already dispatched shipper pickup may finish physically; its final outcome is Completed with cancellation AlreadyCollected. Row locks serialize cancellation, allocation, web session creation/use, unlock dispatch, expiration and completion. The reservation worker skips pending cancellations.

## Device limitation and integration

The current firmware does not provide authenticated offline credential revocation evidence. This implementation therefore always returns `canRetrieve: false`; it does not expose a resident retrieval unlock or claim a deposited return was safely cancelled. Operator follow-up, trustworthy revocation and a verified resident physical retrieval/door cycle remain necessary before that part of #192 can be completed. No firmware signing protocol, reset success, door state or physical delivery result is invented.

This branch starts at dev and adds only ReturnCancellationRequest plus its migration. It must be reconciled with the additive migrations in other feature PRs before deployment. No database migration was applied. When integrating with existing PR166, exclude returns with `Cancellation != null` from OfflinePinService grant issuance, inventory/pins/workflow payload builders and shipper credential retrieval. Offline device logs representing an already completed pickup remain physical evidence to review; cancellation must not rewrite them as successful owner retrieval. PR166 reset acknowledgement remains credential-only and does not complete cancellation.

Mobile62 reads this contract, hides sharing during requests/uncertainty, resumes pending status and provides operator contact guidance. Shipper28 clears revoked sessions and revalidates before every unlock. Full deposited cancellation remains blocked by the device evidence limitation, so issues remain open.

Guest pickup GET returns `completed: true` only for a physically completed return and its still-valid matching session token, with image data removed. A 409/401/403/404/410 is never completion evidence. The completed token hash is retained for the existing guest timeout, authorizes completion reads only and cannot unlock again. Cancellation always clears it.
