# Validation — 0.2.0

Release build and 22 test groups passed locally on Windows with .NET Framework 4.8 and MSBuild 18. No public endpoints were contacted by the tests.

Coverage includes strict target parsing; deduplication; input cancellation; NLA, TLS and legacy responses; negotiation failure codes; malformed headers; 20,000 random parser inputs; fragmented and truncated socket replies; a whole-attempt timeout; socket cancellation; connection refusal; bounded concurrency and exact-once accounting for 1,000 targets; output classification; selective retries; queued/in-flight cancellation recovery; visible worker failures; empty sessions; repeat-run rejection; and output-file collision protection.

The actual Release application was launched and its initial window inspected. The screenshot in the README is from that executable, including the corrected white text on disabled buttons. A full interactive load/start/stop workflow was not completed in this session. Network and session behavior were exercised by the automated tests. High-DPI layouts, long-duration production runs and real Windows RDP server combinations have not been verified here.

## Dispatch benchmark

The test runner compares the previous `List<string>.RemoveAt(0)` dispatch pattern with an atomic next-index counter on 100,000 items. One local Release run measured:

| Dispatch operation | Elapsed |
| --- | ---: |
| Repeated `RemoveAt(0)` | 1,352.63 ms |
| Atomic index | 0.46 ms |

This isolates dispatch overhead. It is **not** an end-to-end scan speedup and does not measure network throughput. Actual scan time depends on network latency, timeout, retries and available sockets. The old dispatch path shifts the remaining list repeatedly, giving quadratic total work; the new path has linear total work.

## فارسی

بیلد Release و ۲۲ گروه تست محلی موفق بودند. علاوه بر پارسر و ورودی‌ها، رفتار شبکه با سرورهای loopback، توقف اتصال فعال، تلاش مجدد، ثبت دقیق هزار ورودی هم‌زمان و حفظ بررسی‌های نیمه‌تمام آزمایش شد. هیچ میزبان عمومی برای تست اسکن نشد.

برنامهٔ ساخته‌شده اجرا و ظاهر اولیه‌اش بررسی شد. عکس README از همان فایل اجرایی است. چرخهٔ کامل انتخاب فایل، شروع و توقف از طریق رابط در این جلسه کامل نشد؛ منطق شبکه و جلسه با تست خودکار بررسی شد. اجرای طولانی، مقیاس‌های مختلف DPI و ترکیب‌های واقعی سرورهای RDP هنوز نیاز به آزمایش دارند.

زمان ۱۳۵۲٫۶۳ در برابر ۰٫۴۶ میلی‌ثانیه مربوط به توزیع داخلی ۱۰۰هزار کار است، نه سرعت اسکن شبکه. این عدد تضمین افزایش سرعت اینترنت یا سرور مقصد نیست.
