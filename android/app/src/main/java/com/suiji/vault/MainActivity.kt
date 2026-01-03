
package com.suiji.vault
import android.app.Activity
import android.os.Bundle
import android.widget.TextView
class MainActivity: Activity() {
  override fun onCreate(b: Bundle?) {
    super.onCreate(b)
    val tv = TextView(this)
    tv.text = "随记（Suiji Vault）Android\n自动同步工程骨架"
    setContentView(tv)
  }
}
