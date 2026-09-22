Yasser Subtitle Studio 1.0.0
============================

التشغيل:
- شغّل YasserSubtitleStudio.exe.
- البرنامج مبني فوق محرر Subtitle Edit الأصلي ويحافظ على أدواته الأساسية وأيقونته الأصلية.
- من قائمة "Yasser Subtitle Studio" تستطيع فتح مشروع Yasser للفيديو الحالي.
- إذا لم تكن أدوات Whisper وFFmpeg موجودة، افتح خيار إعداد المحركات وثبّتها مرة واحدة.
- بعد التفريغ اختر لغة الترجمة المطلوبة ثم ابدأ الترجمة.
- كل سطر مكتمل يُحفظ في مشروع .yssproj ويمكن استئناف العمل لاحقاً.
- خيار "تطبيق نتيجة Yasser على جدول Subtitle Edit" ينقل النص/الترجمة المحفوظة إلى الجدول الأصلي.
- يمكن تصدير SRT من أدوات Yasser أو من أدوات Subtitle Edit الأصلية.

الخصوصية:
- التعرف على الكلام يعمل محلياً بواسطة Whisper/FFmpeg.
- الترجمة النصية عبر الإنترنت ترسل نص السطر فقط إلى مزود الترجمة، ولا ترفع الفيديو أو الصوت.

الملفات:
- مشاريع الدمج تحفظ تحت LocalAppData\YasserSubtitleStudio\IntegratedProjects.
- أدوات Whisper/FFmpeg تحفظ تحت LocalAppData\YasserSubtitleStudio\tools.

حقوق المصدر:
- يتضمن البرنامج أجزاء من Subtitle Edit وفق رخصة MIT.
- راجع UPSTREAM-LICENSE.txt و NOTICE-YASSER-UPSTREAM.txt داخل الحزمة.
