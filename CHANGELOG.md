# Changes / تغییرات

## 0.2.0 — 2026-11-13

### English

- Fixed reversed `NLA.txt` / `No_NLA.txt` output and incorrect NLA classification of negotiation failures.
- Replaced front-of-list removal, dedicated worker threads and polling loops with bounded async workers and constant-time work assignment.
- Fixed lost counter increments, cross-thread control updates, duplicate starts and timers that continued after completion.
- Added one deadline for the complete connection attempt, cancellation of active sockets and correct handling of fragmented or truncated responses.
- Stop now retains both queued and interrupted addresses. Each run gets a unique result folder; file errors are reported instead of silently discarded.
- Added strict IPv4/IPv6 parsing, port validation, deduplication and input counts.
- Added separate negotiation/non-RDP/error output and a diagnostic CSV. Retries apply only to unavailable endpoints.
- Rebuilt the interface using standard WinForms controls; button text stays white, including disabled buttons.
- Removed the external Bunifu reference, unused packages and the recursive thread helper. Added build/package scripts, automated checks, an application screenshot and English/Persian documentation.

Compatibility: output meanings are corrected compared with 0.1.1. `No_NLA.txt` means standard RDP or TLS was selected; it is not proof of the server's global NLA policy. Results now live under Local AppData. The retry field is explicitly the **total number of attempts**. Invalid and duplicate input lines are excluded before scanning.

### فارسی

- جابه‌جایی فایل‌های NLA و بدون NLA اصلاح شد؛ خطاهای مذاکره دیگر همگی NLA حساب نمی‌شوند.
- حذف از ابتدای لیست، نخ‌های جداگانه و حلقه‌های انتظار جای خود را به کارگرهای async با هم‌زمانی محدود دادند.
- شمارنده‌های نادقیق، دسترسی نخ‌های پس‌زمینه به کنترل‌ها، شروع تکراری و تایمرهای متوقف‌نشده اصلاح شدند.
- زمان انتظار کل تلاش اتصال را پوشش می‌دهد. توقف، اتصال‌های فعال را می‌بندد و پاسخ‌های تکه‌تکه یا ناقص درست پردازش می‌شوند.
- آدرس‌های در صف و بررسی‌های نیمه‌تمام در فایل بررسی‌نشده‌ها باقی می‌مانند. هر اجرا پوشهٔ جدا دارد و خطای فایل مخفی نمی‌شود.
- اعتبارسنجی IPv4/IPv6 و پورت، حذف تکراری‌ها، آمار ورودی، خروجی تفکیک‌شده و CSV اضافه شدند.
- رابط با کنترل‌های استاندارد ویندوز بازسازی شد. متن دکمه‌ها، حتی در حالت غیرفعال، سفید است.
- وابستگی بیرونی Bunifu، بسته‌های بلااستفاده و کمک‌کلاس بازگشتی نخ‌ها حذف شدند. اسکریپت ساخت و بسته‌بندی، تست، تصویر برنامه و راهنمای دو زبانه اضافه شد.

نکتهٔ مهاجرت: معنی خروجی‌ها نسبت به نسخهٔ ۰٫۱٫۱ اصلاح شده است. فایل `No_NLA.txt` فقط انتخاب RDP معمولی یا TLS در همین مذاکره را نشان می‌دهد. خروجی‌ها به Local AppData منتقل شده‌اند و فیلد تلاش، تعداد کل تلاش‌ها را مشخص می‌کند.
