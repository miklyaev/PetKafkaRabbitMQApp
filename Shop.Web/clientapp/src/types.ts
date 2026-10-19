export type OrderStatus = 'Accepted' | 'Completed' | 'Cancelled';

export interface Product {
  name: string;
  price: number;
  icon: string;
}

export interface OrderSummary {
  id: string;
  customerName: string;
  status: OrderStatus;
  totalAmount: number;
  createdAt: string;
  items: string[];
}

export interface CreateOrderResult {
  orderId: string;
  status: OrderStatus;
  totalAmount: number;
}

export interface NotificationPayload {
  orderId: string;
  customerName: string;
  status: OrderStatus;
  text: string;
  occurredAt: string;
}
