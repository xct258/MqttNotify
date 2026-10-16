package com.xct258.mqttnotify.mqtt

import java.security.KeyStore
import java.security.Provider
import java.security.cert.X509Certificate
import javax.net.ssl.ManagerFactoryParameters
import javax.net.ssl.TrustManager
import javax.net.ssl.TrustManagerFactory
import javax.net.ssl.TrustManagerFactorySpi
import javax.net.ssl.X509TrustManager

/**
 * 「信任所有证书」的 [TrustManagerFactory]，用于 WSS + 自签证书。
 *
 * 为什么自己写：HiveMQ 的 `MqttClientSslConfig` 只接受 [TrustManagerFactory]，
 * 而它依赖的 netty 在 POM 里是 `runtime` scope（编译期不可见），
 * 所以不能用 Netty 现成的 `InsecureTrustManagerFactory`。这里只用 JDK API，不引入新依赖。
 *
 * **安全性**：跳过证书链校验，等于放弃对服务端身份的验证，仅限内网自签证书场景，
 * 界面上默认关闭并有醒目告警。
 */
object InsecureSsl {

    /** 放行一切证书（不做任何链校验、域名校验）。 */
    private val trustAllManager: X509TrustManager = object : X509TrustManager {
        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit

        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit

        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }

    /**
     * 借 [TrustManagerFactory] 的 protected 构造器造一个匿名子类，
     * 直接返回放行一切的 [trustAllManager]。JDK 与 Android(Conscrypt) 的实现一致。
     */
    fun trustManagerFactory(): TrustManagerFactory {
        val spi = object : TrustManagerFactorySpi() {
            override fun engineInit(keyStore: KeyStore?) = Unit

            override fun engineInit(spec: ManagerFactoryParameters?) = Unit

            override fun engineGetTrustManagers(): Array<TrustManager> = arrayOf(trustAllManager)
        }
        return object : TrustManagerFactory(
            spi,
            null as Provider?,
            TrustManagerFactory.getDefaultAlgorithm(),
        ) {}
    }
}
