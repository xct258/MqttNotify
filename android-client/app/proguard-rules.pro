# HiveMQ MQTT Client / Netty 在 R8 下的防混淆与缺类容错
# （Netty 内部通过反射 / ServiceLoader 加载部分实现，压缩后需保留）

-keep class com.hivemq.** { *; }
-keep interface com.hivemq.** { *; }
-dontwarn com.hivemq.**

-keep class io.netty.** { *; }
-keep interface io.netty.** { *; }
-dontwarn io.netty.**

-keep class org.reactivestreams.** { *; }
-dontwarn org.reactivestreams.**

-dontwarn org.slf4j.**
-dontwarn reactor.**
-dontwarn dagger.**
-dontwarn javax.annotation.**
-dontwarn org.codehaus.mojo.**
-dontwarn java.lang.management.**
-dontwarn sun.misc.**

# 保留 ServiceLoader 元数据
-keepattributes *Annotation*, Signature, InnerClasses, EnclosingMethod
