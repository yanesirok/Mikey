package com.mikey.pose;

import androidx.annotation.NonNull;
import androidx.lifecycle.Lifecycle;
import androidx.lifecycle.LifecycleOwner;
import androidx.lifecycle.LifecycleRegistry;

/**
 * Minimal {@link LifecycleOwner} so CameraX can be bound inside a Unity Activity, which is
 * not itself a LifecycleOwner. Driven manually from {@link PoseSession#start()} / stop().
 */
public class SimpleLifecycleOwner implements LifecycleOwner {

    private final LifecycleRegistry registry = new LifecycleRegistry(this);

    public void start() {
        registry.setCurrentState(Lifecycle.State.RESUMED);
    }

    public void stop() {
        registry.setCurrentState(Lifecycle.State.DESTROYED);
    }

    @NonNull
    @Override
    public Lifecycle getLifecycle() {
        return registry;
    }
}
