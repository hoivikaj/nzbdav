import type { HTMLAttributes, ReactNode } from "react";

type AlertVariant = "info" | "success" | "warning" | "danger";

const alertVariants: Record<AlertVariant, string> = {
  info: "alert-info",
  success: "alert-success",
  warning: "alert-warning",
  danger: "alert-error",
};

export function Alert({
  variant = "info",
  className = "",
  ...props
}: HTMLAttributes<HTMLDivElement> & { variant?: AlertVariant }) {
  return (
    <div
      role="alert"
      className={`alert ${alertVariants[variant]} ${className}`}
      {...props}
    />
  );
}

export function Badge({ className = "", ...props }: HTMLAttributes<HTMLSpanElement>) {
  return <span className={`badge ${className}`} {...props} />;
}

export function Spinner({ className = "", size }: { className?: string; size?: string }) {
  return <span className={`loading loading-spinner ${size === "sm" ? "loading-sm" : ""} ${className}`} />;
}

export function Tooltip({ content, children }: { content: string; children: ReactNode }) {
  return (
    <span className="tooltip" data-tip={content}>
      {children}
    </span>
  );
}
